﻿using System;
using System.Data.SqlClient;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Polly;

namespace Extensions
{
    public static class IWebHostExtensions
    {
        public static bool IsInKubernetes(this IWebHost webHost)
        {
            var cfg = webHost.Services.GetService<IConfiguration>();
            return CheckIfK8s(cfg);
        }

        private static bool CheckIfK8s(IConfiguration cfg)
        {
            var orchestratorType = cfg.GetValue<string>("OrchestratorType");
            return orchestratorType?.ToUpper() == "K8S";
        }

        public static IWebHost MigrateDbContext<TContext>(this IWebHost webHost, Action<TContext, IServiceProvider> seeder) where TContext : DbContext
        {
            var underK8s = webHost.IsInKubernetes();

            using var scope = webHost.Services.CreateScope();

            TryMigrateAndSeed(scope, underK8s, seeder);

            return webHost;
        }

        public static bool IsInKubernetes(this Microsoft.Extensions.Hosting.IHost host)
        {
            var cfg = host.Services.GetService<IConfiguration>();
            return CheckIfK8s(cfg);
        }

        public static Microsoft.Extensions.Hosting.IHost MigrateDbContext<TContext>(this Microsoft.Extensions.Hosting.IHost webHost, Action<TContext, IServiceProvider> seeder) where TContext : DbContext
        {
            var underK8s = webHost.IsInKubernetes();

            using var scope = webHost.Services.CreateScope();
            TryMigrateAndSeed(scope, underK8s, seeder);

            return webHost;
        }

        private static void TryMigrateAndSeed<TContext>(IServiceScope scope, bool underK8s, Action<TContext, IServiceProvider> seeder) where TContext : DbContext
        {
            var services = scope.ServiceProvider;

            var logger = services.GetRequiredService<ILogger<TContext>>();

            var context = services.GetService<TContext>();

            try
            {
                logger.LogInformation("Migrating database associated with context {DbContextName}", typeof(TContext).Name);

                if (underK8s)
                {
                    InvokeSeeder(seeder, context, services);
                }
                else
                {
                    var retry = Policy.Handle<SqlException>()
                         .WaitAndRetry(new TimeSpan[]
                         {
                             TimeSpan.FromSeconds(3),
                             TimeSpan.FromSeconds(5),
                             TimeSpan.FromSeconds(8),
                         });

                    //if the sql server container is not created on run docker compose this
                    //migration can't fail for network related exception. The retry options for DbContext only 
                    //apply to transient exceptions
                    // Note that this is NOT applied when running some orchestrators (let the orchestrator to recreate the failing service)
                    retry.Execute(() => InvokeSeeder(seeder, context, services));
                }

                logger.LogInformation("Migrated database associated with context {DbContextName}", typeof(TContext).Name);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An error occurred while migrating the database used on context {DbContextName}", typeof(TContext).Name);
                if (underK8s)
                {
                    throw;          // Rethrow under k8s because we rely on k8s to re-run the pod
                }
            }
        }

        private static void InvokeSeeder<TContext>(Action<TContext, IServiceProvider> seeder, TContext context, IServiceProvider services)
            where TContext : DbContext
        {
            context.Database.Migrate();
            seeder(context, services);
        }
    }
}
