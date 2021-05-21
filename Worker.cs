using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;


namespace Scrimp
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        public Worker(ILogger<Worker> logger)
        {
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Worker running at {time}", DateTimeOffset.Now);
                
                //do work here
                var username = "my@email.com";
                var pw = "";
                var episodeService = new EpisodeService(new Logger<EpisodeService>(), username, pw );


                await Task.Delay(1000, stoppingToken);
            }
        }
    }
}
