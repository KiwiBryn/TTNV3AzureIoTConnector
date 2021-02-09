//---------------------------------------------------------------------------------
// Copyright (c) January 2021, devMobile Software
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
//---------------------------------------------------------------------------------
namespace devMobile.TheThingsNetwork.WorkerService
{
   using System;
   using Microsoft.Extensions.DependencyInjection;
   using Microsoft.Extensions.Hosting;

   using Microsoft.Extensions.Logging;
   using Microsoft.Extensions.Options;

   public class Program
   {
      public static void Main(string[] args)
      {
         CreateHostBuilder(args).Build().Run();
      }

      public static IHostBuilder CreateHostBuilder(string[] args) =>
         Host.CreateDefaultBuilder(args)
         .ConfigureLogging(logging =>
         {
            //logging.AddApplicationInsights();
            logging.ClearProviders();
            logging.AddConsole();
         })
         .ConfigureServices((hostContext, services) =>
         {
            services.Configure<ProgramSettings>(hostContext.Configuration.GetSection("ProgramSettings"))
               .AddHostedService<Worker>()
               .AddApplicationInsightsTelemetryWorkerService()
               .AddTransient<ProgramSettings>(a => a.GetRequiredService<IOptions<ProgramSettings>>().Value);
         });
     
   }
}