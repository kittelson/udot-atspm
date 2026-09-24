#region license
// Copyright 2026 Utah Departement of Transportation
// for Infrastructure - Utah.Udot.Atspm.Infrastructure.Services.HostedServices/DecodeEventLogHostedService.cs
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
// http://www.apache.org/licenses/LICENSE-2.
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
#endregion

using Lextm.SharpSnmpLib.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks.Dataflow;
using Utah.Udot.ATSPM.Infrastructure.Workflows;
using Utah.Udot.Atspm.Repositories.EventLogRepositories;

namespace Utah.Udot.Atspm.Infrastructure.Services.HostedServices
{
    public class DecodeEventLogHostedService(ILogger<DecodeEventLogHostedService> log, IServiceScopeFactory serviceProvider, IOptions<DecodeEventsConfiguration> options) : HostedServiceBase(log, serviceProvider)
    {
        private const string CubicFileTimestampFormat = "yyyy_MM_dd_HHmm";

        private readonly IOptions<DecodeEventsConfiguration> _options = options;

        /// <inheritdoc/>
        public override async Task Process(IServiceScope scope, Stopwatch stopwatch, CancellationToken cancellationToken = default)
        {
            var repo = scope.ServiceProvider.GetService<IDeviceRepository>();
            var devices = repo?.GetList().ToList() ?? new List<Device>();

            var workflow = new DecodeEventLogWorkflow(scope.ServiceProvider.GetService<IServiceScopeFactory>(), 50000, cancellationToken);
            await workflow.Initialize();

            Console.WriteLine($"path: {_options.Value.Path}");

            var dir = new DirectoryInfo(_options.Value.Path);

            if (dir.Exists)
            {
                var files = dir.GetFiles("", SearchOption.AllDirectories);

                Console.WriteLine($"file count {files.Length}");

                var groups = files.GroupBy(f => f.Directory.Name);

                foreach (var g in groups)
                {
                    if (g.Key.Contains('-'))
                    {
                        var tag = new[] { g.Key }.Select(s => s.Split('-')).Select(parts => new
                        {
                            Id = parts[0],
                            Ip = parts[1]
                        })
                            .FirstOrDefault();

                        var device = devices.FirstOrDefault(f => f.DeviceIdentifier == tag.Id);

                        if (device != null)
                        {
                            foreach (var f in g)
                            {
                                await workflow.Input.SendAsync(Tuple.Create(device, f));
                            }
                        }
                    }
                }

                var latestImportedCubicEvents = await GetLatestImportedCubicEvents(scope.ServiceProvider, devices, cancellationToken);
                await QueueCubicFiles(workflow, devices, latestImportedCubicEvents);
            }
            else
            {
                Console.WriteLine($"directory {_options.Value.Path} doesn't exist");
            }

            workflow.Input.Complete();

            await Task.WhenAll(workflow.Steps.Select(s => s.Completion));
        }

        private static bool IsCubicDevice(Device device)
        {
            return device?.DeviceConfiguration?.Description?.StartsWith("Cubic", StringComparison.OrdinalIgnoreCase) == true;
        }

        private static async Task<Dictionary<string, DateTime>> GetLatestImportedCubicEvents(IServiceProvider serviceProvider, IReadOnlyCollection<Device> devices, CancellationToken cancellationToken)
        {
            var locationIdentifiers = devices
                .Where(IsCubicDevice)
                .Select(d => d.Location?.LocationIdentifier)
                .Where(d => !string.IsNullOrWhiteSpace(d))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (locationIdentifiers.Count == 0)
            {
                return new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            }

            var eventRepository = serviceProvider.GetService<IIndianaEventLogRepository>();
            if (eventRepository == null)
            {
                return new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            }

            return await eventRepository.GetLatestHourByLocations(locationIdentifiers, cancellationToken);
        }

        private static async Task QueueCubicFiles(DecodeEventLogWorkflow workflow, IReadOnlyCollection<Device> devices, IReadOnlyDictionary<string, DateTime> latestImportedCubicEvents)
        {
            var cubicDevices = devices.Where(IsCubicDevice).ToList();

            var rootPath = cubicDevices.FirstOrDefault()?.DeviceConfiguration?.Path;

            if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
            {
                Console.WriteLine($"Skipping Cubic log import because UNC root path {rootPath} was not accessible.");
                return;
            }

            foreach (var device in cubicDevices)
            {
                var cubicId = device.DeviceProperties?.FirstOrDefault(p => p.Key.Equals("ATMSNOWID", StringComparison.OrdinalIgnoreCase)).Value?.ToString();

                var controllerFolder = Path.Combine(rootPath, $"Ctrl{cubicId}");
                if (!Directory.Exists(controllerFolder))
                {
                    Console.WriteLine($"Skipping Cubic device {device.DeviceIdentifier} because controller folder {controllerFolder} was not accessible.");
                    continue;
                }

                var hasLatestImportedEvent = latestImportedCubicEvents.TryGetValue(device.Location?.LocationIdentifier ?? string.Empty, out var latestImportedEvent);
                DateTime? latestImportedEventTimestamp = hasLatestImportedEvent ? latestImportedEvent : null;

                foreach (var file in Directory.GetFiles(controllerFolder, "*", SearchOption.AllDirectories)
                    .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase))
                {
                    var fileInfo = new FileInfo(file);
                    if (ShouldSkipCubicFile(fileInfo, latestImportedEventTimestamp))
                    {
                        Console.WriteLine($"Skipping Cubic file {fileInfo.FullName} because it is older than the latest imported hour {latestImportedEventTimestamp:O}.");
                        continue;
                    }

                    await workflow.Input.SendAsync(Tuple.Create(device, fileInfo));
                }
            }
        }

        private static bool ShouldSkipCubicFile(FileInfo file, DateTime? latestImportedEventTimestamp)
        {
            if (!latestImportedEventTimestamp.HasValue || !file.Extension.Equals(".csv", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return TryGetCubicFileTimestamp(file.Name, out var fileTimestamp) &&
                fileTimestamp < latestImportedEventTimestamp.Value.AddHours(-1);
        }

        private static bool TryGetCubicFileTimestamp(string fileName, out DateTime timestamp)
        {
            var name = Path.GetFileNameWithoutExtension(fileName);
            var parts = name.Split('_', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 6 &&
                parts[0].Equals("TRAF", StringComparison.OrdinalIgnoreCase) &&
                DateTime.TryParseExact($"{parts[2]}_{parts[3]}_{parts[4]}_{parts[5]}", CubicFileTimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out timestamp))
            {
                return true;
            }

            timestamp = default;
            return false;
        }
    }
}
