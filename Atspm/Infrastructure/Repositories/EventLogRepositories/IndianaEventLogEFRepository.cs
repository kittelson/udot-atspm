#region license
// Copyright 2026 Utah Departement of Transportation
// for Infrastructure - Utah.Udot.Atspm.Infrastructure.Repositories.EventLogRepositories/IndianaEventLogEFRepository.cs
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

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Utah.Udot.Atspm.Data;
using Utah.Udot.Atspm.Data.Models.EventLogModels;

namespace Utah.Udot.Atspm.Infrastructure.Repositories.EventLogRepositories
{
    ///<inheritdoc cref="IIndianaEventLogRepository"/>
    public class IndianaEventLogEFRepository : EventLogEFRepositoryBase<IndianaEvent>, IIndianaEventLogRepository
    {
        ///<inheritdoc/>
        public IndianaEventLogEFRepository(EventLogContext db, ILogger<IndianaEventLogEFRepository> log) : base(db, log) { }

        #region IIndiannaEventRepository

        ///<inheritdoc/>
        public async Task<Dictionary<string, DateTime>> GetLatestHourByLocations(IEnumerable<string> locationIdentifiers, CancellationToken cancellationToken = default)
        {
            var locations = locationIdentifiers
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (locations.Count == 0)
            {
                return new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            }

            var now = DateTime.Now;
            var latestEvents = await table
                .AsNoTracking()
                .Where(w => locations.Contains(w.LocationIdentifier) && w.End <= now)
                .GroupBy(g => g.LocationIdentifier)
                .Select(g => new
                {
                    LocationIdentifier = g.Key,
                    Timestamp = g.Max(x => x.End)
                })
                .ToListAsync(cancellationToken);

            return latestEvents.ToDictionary(k => k.LocationIdentifier, v => v.Timestamp, StringComparer.OrdinalIgnoreCase);
        }

        #endregion
    }
}
