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
using System.Threading.Tasks.Dataflow;
using Utah.Udot.ATSPM.Infrastructure.Workflows;

namespace Utah.Udot.Atspm.Infrastructure.Services.HostedServices
{
    public class DecodeEventLogHostedService(ILogger<DecodeEventLogHostedService> log, IServiceScopeFactory serviceProvider, IOptions<DecodeEventsConfiguration> options) : HostedServiceBase(log, serviceProvider)
    {
        private readonly IOptions<DecodeEventsConfiguration> _options = options;

        /// <inheritdoc/>
        public override async Task Process(IServiceScope scope, Stopwatch stopwatch, CancellationToken cancellationToken = default)
        {
            var repo = scope.ServiceProvider.GetService<IDeviceRepository>();

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

                        var device = repo.GetList().FirstOrDefault(f => f.DeviceIdentifier == tag.Id);

                        if (device != null)
                        {
                            foreach (var f in g)
                            {
                                await workflow.Input.SendAsync(Tuple.Create(device, f));
                            }
                        }
                    }
                }

                await QueueCubicFiles(workflow, repo.GetList().ToList());
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
            return device?.DeviceConfiguration?.Product?.Manufacturer?.Equals("Cubic", StringComparison.OrdinalIgnoreCase) == true;
        }

        private static async Task QueueCubicFiles(DecodeEventLogWorkflow workflow, IReadOnlyCollection<Device> devices)
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

                foreach (var file in Directory.GetFiles(controllerFolder, "*", SearchOption.AllDirectories))
                {
                    await workflow.Input.SendAsync(Tuple.Create(device, new FileInfo(file)));
                }
            }
        }
    }
}