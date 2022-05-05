using Es.Riam.Gnoss.Servicios;
using Es.Riam.Gnoss.Util.Configuracion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ServicioNotificaciones;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Gnoss.BackgroundTask.SubscriptionsMail
{
    public class SubscriptionsMailWorker : Worker
    {
        private readonly ILogger<SubscriptionsMailWorker> _logger;
        private readonly ConfigService _configService;

        public SubscriptionsMailWorker(ILogger<SubscriptionsMailWorker> logger, ConfigService configService, IServiceScopeFactory scopeFactory) 
            : base(logger, scopeFactory)
        {
            _logger = logger;
            _configService = configService;
        }

        protected override List<ControladorServicioGnoss> ObtenerControladores()
        {
            List<ControladorServicioGnoss> controladores = new List<ControladorServicioGnoss>();
            controladores.Add(new Controller(ScopedFactory, _configService));
            return controladores;
        }
    }
}
