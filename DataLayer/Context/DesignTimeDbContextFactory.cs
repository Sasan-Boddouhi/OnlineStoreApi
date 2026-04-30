using DataLayer.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DataLayer.Context
{
    /// <summary>
    /// Factory برای زمان طراحی (Migration) – بدون وابستگی به DI و Interceptors
    /// </summary>
    public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext(string[] args)
        {
            var connectionString = "Server=.\\SQLSERVER2014;Database=ShopDB;User Id=sa;Password=nb123456;TrustServerCertificate=True;";

            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            optionsBuilder.UseSqlServer(connectionString);

            var emptyInterceptors = Enumerable.Empty<Microsoft.EntityFrameworkCore.Diagnostics.ISaveChangesInterceptor>();

            return new AppDbContext(optionsBuilder.Options, emptyInterceptors);
        }
    }
}