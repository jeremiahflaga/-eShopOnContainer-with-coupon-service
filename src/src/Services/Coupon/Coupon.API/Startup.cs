using Coupon.API.Extensions;
using Coupon.API.Filters;
using Coupon.API.IntegrationEvents.EventHandlers;
using Coupon.API.IntegrationEvents.Events;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.eShopOnContainers.BuildingBlocks.EventBus.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Coupon.API
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllers(options => options.Filters.Add<ValidateModelAttribute>());

            services.AddCustomSettings(Configuration)
                .AddCouponRegister(Configuration)
                .AddCustomPolicies()
                .AddAppInsights(Configuration)
                .AddEventBus(Configuration)
                .AddCustomAuthentication(Configuration)
                .AddCustomAuthorization()
                .AddSwagger(Configuration)                
                // NOTE_JBOY: from https://docs.microsoft.com/en-us/learn/modules/microservices-aspnet-core/6-add-coupon-service
                // The AddCustomHealthCheck extension method tests external service dependencies 
                // to confirm availability and normal operation.
                .AddCustomHealthCheck(Configuration);

            services.AddTransient<IIntegrationEventHandler<OrderStatusChangedToAwaitingCouponValidationIntegrationEvent>, OrderStatusChangedToAwaitingCouponValidationIntegrationEventHandler>();
            services.AddTransient<IIntegrationEventHandler<OrderStatusChangedToCancelledIntegrationEvent>, OrderStatusChangedToCancelledIntegrationEventHandler>();
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            var pathBase = Configuration["PATH_BASE"];

            if (!string.IsNullOrEmpty(pathBase))
            {
                app.UsePathBase(pathBase);
            }

            app.UseSwagger()
                .UseSwaggerUI(options =>
                {
                    options.SwaggerEndpoint($"{ (!string.IsNullOrEmpty(pathBase) ? pathBase : string.Empty) }/swagger/v1/swagger.json", "Coupon.API V1");
                    options.OAuthClientId("couponswaggerui");
                    options.OAuthAppName("eShop-Learn.Coupon.API Swagger UI");
                })
                .UseCors("CorsPolicy")
                .UseRouting()
                .UseAuthentication()
                .UseAuthorization()
                .UseEndpoints(endpoints =>
                {
                    endpoints.MapControllers();
                    
                    // NOTE_JBOY: from https://docs.microsoft.com/en-us/learn/modules/microservices-aspnet-core/6-add-coupon-service
                    // The preceding change registers two HTTP health check endpoints with the ASP.NET Core routing system:
                    // 1. /liveness: A liveness endpoint that Kubernetes queries periodically to check for failures.
                    // 2. /hc: A readiness endpoint that Kubernetes queries to know when a service is ready to start accepting traffic.
                    //         The same endpoint is also queried by an external health monitoring system, like the WebStatus app.
                    endpoints.MapHealthChecks("/hc", new HealthCheckOptions
                    {
                        Predicate = _ => true,
                        ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
                    });
                    endpoints.MapHealthChecks("/liveness", new HealthCheckOptions
                    {
                        Predicate = r => r.Name.Contains("self")
                    });
                });

            ConfigureEventBus(app);
        }

        private void ConfigureEventBus(IApplicationBuilder app)
        {
            var eventBus = app.ApplicationServices.GetRequiredService<IEventBus>();

            eventBus.Subscribe<OrderStatusChangedToAwaitingCouponValidationIntegrationEvent, IIntegrationEventHandler<OrderStatusChangedToAwaitingCouponValidationIntegrationEvent>>();
            eventBus.Subscribe<OrderStatusChangedToCancelledIntegrationEvent, IIntegrationEventHandler<OrderStatusChangedToCancelledIntegrationEvent>>();
        }
    }
}
