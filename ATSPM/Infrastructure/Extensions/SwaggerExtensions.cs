#region license
// Copyright 2026 Utah Departement of Transportation
// for Infrastructure - Utah.Udot.Atspm.Infrastructure.Extensions/SwaggerExtensions.cs
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

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;
using Swashbuckle.AspNetCore.SwaggerUI;
using System.Reflection;

namespace Utah.Udot.Atspm.Infrastructure.Extensions
{
    public static class SwaggerExtensions
    {
        public static IServiceCollection AddAtspmSwaggerV10(this IServiceCollection services, IConfiguration configuration, Action<SwaggerGenOptions> setupAction)
        {
            services.AddEndpointsApiExplorer();
            services.AddSwaggerGen(options =>
            {
                options.SwaggerDoc("v1", CreateInfo(configuration, "1.0"));
                setupAction(options);
            });

            return services;
        }

        public static IApplicationBuilder UseAtspmSwaggerUIV10(this IApplicationBuilder app, IConfiguration configuration)
        {
            app.UseSwagger();
            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint("v1/swagger.json", GetSwaggerTitle(configuration));
                ApplySwaggerUiConfiguration(options, configuration);
            });

            return app;
        }

        public static IApplicationBuilder UseAtspmSwaggerUIV10(this IApplicationBuilder app, IConfiguration configuration, Action<SwaggerUIOptions> setupAction)
        {
            app.UseSwagger();
            app.UseSwaggerUI(options =>
            {
                ApplySwaggerUiConfiguration(options, configuration);
                setupAction(options);
            });

            return app;
        }

        public static SwaggerGenOptions IncludeAtspmXmlComments(this SwaggerGenOptions swaggerGenOptions, Assembly assembly)
        {
            var xmlFilename = $"{assembly.GetName().Name}.xml";
            var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFilename);
            if (File.Exists(xmlPath))
            {
                swaggerGenOptions.IncludeXmlComments(xmlPath);
            }

            return swaggerGenOptions;
        }

        public static SwaggerGenOptions SetAtspmCustomOperationIds(this SwaggerGenOptions swaggerGenOptions, Func<string, string, string, string> operationIdFactory)
        {
            swaggerGenOptions.CustomOperationIds(description =>
            {
                var controller = description.ActionDescriptor.RouteValues.TryGetValue("controller", out var controllerName)
                    ? controllerName ?? string.Empty
                    : string.Empty;
                var action = description.ActionDescriptor.RouteValues.TryGetValue("action", out var actionName)
                    ? actionName ?? string.Empty
                    : string.Empty;
                var verb = description.HttpMethod ?? HttpMethods.Get;

                return operationIdFactory(controller, verb, action);
            });

            return swaggerGenOptions;
        }

        private static OpenApiInfo CreateInfo(IConfiguration configuration, string version)
        {
            var swagger = configuration.GetSection("Swagger");
            var contact = swagger.GetSection("Contact");
            var license = swagger.GetSection("License");

            return new OpenApiInfo
            {
                Title = swagger["Title"] ?? "API",
                Description = swagger["Description"],
                Version = version,
                Contact = new OpenApiContact
                {
                    Name = contact["Name"],
                    Email = contact["Email"],
                    Url = Uri.TryCreate(contact["Url"], UriKind.Absolute, out var contactUrl) ? contactUrl : null
                },
                License = new OpenApiLicense
                {
                    Name = license["Name"],
                    Url = Uri.TryCreate(license["Url"], UriKind.Absolute, out var licenseUrl) ? licenseUrl : null
                }
            };
        }

        private static string GetSwaggerTitle(IConfiguration configuration) => configuration["Swagger:Title"] ?? "API";

        private static void ApplySwaggerUiConfiguration(SwaggerUIOptions options, IConfiguration configuration)
        {
            var swaggerUi = configuration.GetSection("SwaggerUI");

            if (Enum.TryParse<DocExpansion>(swaggerUi["DocExpansion"], true, out var docExpansion))
            {
                options.DocExpansion(docExpansion);
            }

            if (int.TryParse(swaggerUi["DefaultModelsExpandDepth"], out var defaultModelsExpandDepth))
            {
                options.DefaultModelsExpandDepth(defaultModelsExpandDepth);
            }

            if (bool.TryParse(swaggerUi["DisplayRequestDuration"], out var displayRequestDuration) && displayRequestDuration)
            {
                options.DisplayRequestDuration();
            }
        }
    }
}
