#region license
// Copyright 2026 Utah Departement of Transportation
// for Infrastructure - Utah.Udot.Atspm.Infrastructure.Extensions/ApiVersionRouteConstraintExtensions.cs
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

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Utah.Udot.Atspm.Infrastructure.Extensions
{
    public static class ApiVersionRouteConstraintExtensions
    {
        public static IServiceCollection AddApiVersionRouteConstraint(this IServiceCollection services)
        {
            services.Configure<RouteOptions>(options =>
            {
                options.ConstraintMap["apiVersion"] = typeof(ApiVersionRouteConstraint);
            });

            return services;
        }
    }

    /// <summary>
    /// Route constraint used to register the apiVersion token for versioned routes.
    /// </summary>
    public sealed class ApiVersionRouteConstraint : IRouteConstraint
    {
        /// <inheritdoc />
        public bool Match(HttpContext? httpContext, IRouter? route, string routeKey, RouteValueDictionary values, RouteDirection routeDirection)
        {
            return values.TryGetValue(routeKey, out var value) && value is not null && !string.IsNullOrWhiteSpace(value.ToString());
        }
    }
}
