#region license
// Copyright 2026 Utah Departement of Transportation
// for Infrastructure - Utah.Udot.Atspm.Infrastructure.Services.EventLogDecoders/CubicToIndianaDecoder.cs
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
// http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
#endregion

using System.Globalization;
using Utah.Udot.Atspm.Data.Models.EventLogModels;

namespace Utah.Udot.Atspm.Infrastructure.Services.EventLogDecoders
{
    /// <summary>
    /// Decodes Cubic event log ATMS.Now CSV files to Indiana events.
    /// </summary>
    public class CubicToIndianaDecoder : EventLogDecoderBase<IndianaEvent>
    {
        /// <inheritdoc/>
        public override IEnumerable<IndianaEvent> Decode(Device device, Stream stream, CancellationToken cancelToken = default)
        {
            cancelToken.ThrowIfCancellationRequested();

            if (device == null)
                throw new ArgumentNullException(nameof(device), "Device can not be null");

            if (stream?.Length == 0)
                throw new InvalidDataException("Stream is empty");

            var locationIdentifier = device.Location.LocationIdentifier;
            HashSet<IndianaEvent> decodedLogs = new();

            try
            {
                stream.Position = 0;

                using var reader = new StreamReader(stream, leaveOpen: true);
                while (!reader.EndOfStream)
                {
                    cancelToken.ThrowIfCancellationRequested();

                    var line = reader.ReadLine();

                    if (string.IsNullOrWhiteSpace(line) || !line.Contains(','))
                        continue;

                    var lineSplit = line.Split(',', StringSplitOptions.TrimEntries);
                    if (lineSplit.Length < 3)
                        continue;

                    if (!DateTime.TryParse(lineSplit[0], CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTime timestamp))
                        continue;

                    if (!short.TryParse(lineSplit[1], out short eventCode) || !short.TryParse(lineSplit[2], out short eventParam))
                        continue;

                    decodedLogs.Add(new IndianaEvent
                    {
                        LocationIdentifier = locationIdentifier,
                        Timestamp = timestamp,
                        EventCode = eventCode,
                        EventParam = eventParam
                    });
                }
            }
            catch (Exception e)
            {
                throw new EventLogDecoderException(e);
            }

            return decodedLogs;
        }
    }
}
