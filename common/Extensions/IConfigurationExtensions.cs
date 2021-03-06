﻿using Microsoft.Extensions.Configuration;

namespace Extensions
{
    public static class IConfigurationExtensions
    {
        public static TModel GetOptions<TModel>(this IConfiguration configuration, string section) where TModel : new()
        {
            var model = new TModel();
            configuration.GetSection(section).Bind(model);

            return model;
        }

        public static TModel GetOptions<TModel>(this IConfiguration configuration) where TModel : new()
        {
            var model = new TModel();
            configuration.GetSection(typeof(TModel).Name).Bind(model);

            return model;
        }
    }
}
