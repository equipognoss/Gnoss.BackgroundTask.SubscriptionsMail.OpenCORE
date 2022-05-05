using System;
using System.Web;
using System.Collections.Generic;
using System.Text;
using Es.Riam.Gnoss.Util.General;
using Es.Riam.Gnoss.Elementos.Suscripcion;
using Es.Riam.Gnoss.Elementos.Notificacion;
using Es.Riam.Gnoss.Elementos.ServiciosGenerales;
using Es.Riam.Gnoss.Logica.Notificacion;
using Es.Riam.Gnoss.Elementos.Documentacion;
using Es.Riam.Gnoss.Recursos;
using Es.Riam.Gnoss.Logica.ServiciosGenerales;
using Es.Riam.Gnoss.Logica.Suscripcion;
using Es.Riam.Gnoss.Logica.Documentacion;
using System.Threading;
using Es.Riam.Gnoss.Logica.ParametroAplicacion;
using Es.Riam.Util;
using Es.Riam.Gnoss.Elementos.Identidad;
using Es.Riam.Gnoss.Logica.Identidad;
using Es.Riam.Gnoss.AD.Suscripcion;
using Es.Riam.Gnoss.AD.Live;
using System.Data;
using Es.Riam.Gnoss.Servicios;
using Es.Riam.Gnoss.CL.Live;
using Es.Riam.Gnoss.Elementos.Tesauro;
using Es.Riam.Gnoss.Logica.Parametro;
using Es.Riam.Gnoss.CL.ServiciosGenerales;
using Es.Riam.Gnoss.AD.Parametro;
using Es.Riam.Gnoss.AD.EntityModel;
using System.Linq;
using Es.Riam.Gnoss.Web.Controles.ParametroAplicacionGBD;
using Es.Riam.Gnoss.Elementos.ParametroAplicacion;
using Es.Riam.Gnoss.AD.EncapsuladoDatos;
using Microsoft.Extensions.DependencyInjection;
using Es.Riam.Gnoss.Util.Configuracion;
using Es.Riam.Gnoss.AD.EntityModelBASE;
using Es.Riam.Gnoss.AD.Virtuoso;
using Es.Riam.Gnoss.CL;
using Es.Riam.AbstractsOpen;

namespace ServicioNotificaciones
{
    public class Controller : ControladorServicioGnoss
    {
        #region Miembros

        private List<Guid> mListaPerfiles;
        private List<Guid> mListaIdRecursosListados = new List<Guid>();

        #endregion

        #region Constructores

        public Controller(IServiceScopeFactory serviceScopeFactory, ConfigService configService)
            : base(serviceScopeFactory, configService)
        {
        }

        #endregion

        protected override ControladorServicioGnoss ClonarControlador()
        {
            return new Controller(ScopedFactory, mConfigService);
        }

        #region Metodos publicos


        private void EstablecerDominioCache(EntityContext entityContext, LoggingService loggingService, IServicesUtilVirtuosoAndReplication servicesUtilVirtuosoAndReplication)
        {
            bool correcto = false;
            while (!correcto)
            {
                try
                {
                    ParametroAplicacionCN parametroApliCN = new ParametroAplicacionCN(entityContext, loggingService, mConfigService, servicesUtilVirtuosoAndReplication);
                    ParametroAplicacionGBD gestorParamatroAppController = new ParametroAplicacionGBD(loggingService, entityContext, mConfigService);
                    GestorParametroAplicacion gestorParametroAplicacion = new GestorParametroAplicacion();
                    gestorParamatroAppController.ObtenerConfiguracionGnoss(gestorParametroAplicacion);
                    //parametroApliCN.Dispose();

                    //mDominio = ((ParametroAplicacionDS.ParametroAplicacionRow)paramApliDS.ParametroAplicacion.Select("Parametro='UrlIntragnoss'")[0]).Valor;
                    mDominio = gestorParametroAplicacion.ParametroAplicacion.Where(parametroAplicacoin => parametroAplicacoin.Parametro.Equals("UrlIntragnoss")).FirstOrDefault().Valor;
                    mDominio = mDominio.Replace("http://", "").Replace("www.", "");

                    if (mDominio[mDominio.Length - 1] == '/')
                    {
                        mDominio = mDominio.Substring(0, mDominio.Length - 1);
                    }
                    correcto = true;
                }
                catch (Exception ex)
                {
                    loggingService.GuardarLog(loggingService.DevolverCadenaError(ex, "1.0"));
                    Thread.Sleep(1000);
                }
            }
        }

        /// <summary>
        /// Realiza el envio de las notificaciones pendientes de enviar y escribe en el fichero de log una entrada, indicando el resultado de la operacion. Ademas comprueba todos los proyectos que esten en Estado => (Cerrandose = 4) y se encarga de cerrar dichos proyectos si se han sobrepasado sus dias de gracia
        /// </summary>
        /// <param name="attemp"></param>
        /// <returns>Estado de la operacion realizada</returns>
        public override void RealizarMantenimiento(EntityContext entityContext, EntityContextBASE entityContextBASE, UtilidadesVirtuoso utilidadesVirtuoso, LoggingService loggingService, RedisCacheWrapper redisCacheWrapper, GnossCache gnossCache, VirtuosoAD virtuosoAD, IServicesUtilVirtuosoAndReplication servicesUtilVirtuosoAndReplication)
        {
            EstablecerDominioCache(entityContext, loggingService, servicesUtilVirtuosoAndReplication);

            while (true)
            {
                try
                {
                    ComprobarCancelacionHilo();

                    #region Notificaciones de Suscripciones

                    //(Re)Carga los datos de la BD referentes a suscripciones
                    this.CargarPerfilesConSuscripcion(loggingService, entityContext, servicesUtilVirtuosoAndReplication);
                    //Escribe entrada en Log
                    loggingService.GuardarLog(LogStatus.Correcto.ToString().ToUpper() + " (" + this.NombreBD + ") " + this.CrearEntradaRegistro(LogStatus.Correcto, mListaPerfiles.Count.ToString() + " Perfiles con suscripciones"));

                    LogStatus estadoProcesoNotificacion = LogStatus.Error;
                    string entradaLog = string.Empty;

                    //Envio y Log Notificaciones
                    mListaIdRecursosListados.Clear();
                    estadoProcesoNotificacion = this.GenerarNotificacionesDeSuscripciones(entityContext, loggingService, redisCacheWrapper, virtuosoAD, servicesUtilVirtuosoAndReplication);

                    switch (estadoProcesoNotificacion)
                    {
                        case LogStatus.Correcto:
                            entradaLog = LogStatus.Enviando.ToString().ToUpper() + " (" + this.NombreBD + ") " + this.CrearEntradaRegistro(estadoProcesoNotificacion, "Todas las notificaciones de suscripciones generadas");
                            break;
                        case LogStatus.NoGenerado:
                            entradaLog = LogStatus.Enviando.ToString().ToUpper() + " (" + this.NombreBD + ") " + this.CrearEntradaRegistro(estadoProcesoNotificacion, "No hay notificaciones de suscripciones que generar");
                            break;
                        case LogStatus.Error:
                            entradaLog = LogStatus.Enviando.ToString().ToUpper() + " (" + this.NombreBD + ") " + this.CrearEntradaRegistro(estadoProcesoNotificacion, "Notificaciones de suscripciones generadas, pero con errores");
                            break;
                    }
                    //Escribe entrada en Log
                    loggingService.GuardarLog(entradaLog);

                    #endregion

                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    loggingService.GuardarLog(LogStatus.Error.ToString().ToUpper() + " (" + this.NombreBD + ") " + this.CrearEntradaRegistro(LogStatus.Error, ex.Message));
                }
                finally
                {
                    //Duermo el proceso el tiempo establecido
                    GC.Collect();
                    Thread.Sleep(Controller.INTERVALO_SEGUNDOS * 1000);
                }
            }
        }

        #endregion

        #region Metodos privados

        /// <summary>
        /// Genera las notificaciones
        /// </summary>
        /// <returns>Estado del resultado de la operacion de la generación de las notificaciones</returns>
        private LogStatus GenerarNotificacionesDeSuscripciones(EntityContext entityContext, LoggingService loggingService, RedisCacheWrapper redisCacheWrapper, VirtuosoAD virtuosoAD, IServicesUtilVirtuosoAndReplication servicesUtilVirtuosoAndReplication)
        {
            LogStatus estadoProceso = LogStatus.NoGenerado;

            if (mListaPerfiles.Count <= 0)
            {
                return LogStatus.NoGenerado;
            }
            IdentidadCN identCN = new IdentidadCN(entityContext, loggingService, mConfigService, servicesUtilVirtuosoAndReplication);
            PersonaCN persCN = new PersonaCN(entityContext, loggingService, mConfigService, servicesUtilVirtuosoAndReplication);
            OrganizacionCN orgCN = new OrganizacionCN(entityContext, loggingService, mConfigService, servicesUtilVirtuosoAndReplication);

            ParametroCN paramCN = new ParametroCN(entityContext, loggingService, mConfigService, servicesUtilVirtuosoAndReplication);
            List<Guid> listaProyectosConNotificacionesActivas = paramCN.ObtenerListaProyectosConNotificacionesDeSuscripciones();
            paramCN.Dispose();

            foreach (Guid perfilID in mListaPerfiles)
            {
                try
                {
                    GestionPersonas gestPers = new GestionPersonas(persCN.ObtenerPersonaPorPerfil(perfilID), loggingService, entityContext);

                    GestionOrganizaciones gestOrg = new GestionOrganizaciones(orgCN.ObtenerOrganizacionPorPerfil(perfilID), loggingService, entityContext);

                    SuscripcionCN suscCN = new SuscripcionCN(entityContext, loggingService, mConfigService, servicesUtilVirtuosoAndReplication);
                    DataWrapperSuscripcion suscDW = suscCN.ObtenerSuscripcionesDePerfil(perfilID, false);

                    GestionSuscripcion GestorSuscripciones = new GestionSuscripcion(suscDW, loggingService, entityContext);
                    suscCN.Dispose();

                    DateTime fechaActual = DateTime.Today;
                    DateTime fecha = fechaActual;

                    foreach (Suscripcion susc in GestorSuscripciones.ListaSuscripciones.Values)
                    {
                        //Obtenemos la fecha a partir de la cual si tiene boletin, es el boletín actual.
                        //Para las suscripciones semanales, el boletín se generará los lunes
                        if (susc.Tipo != TipoSuscripciones.Comunidades)
                        {
                            continue;
                        }
                        if (susc.FilaSuscripcion.Periodicidad == (int)PeriodicidadSuscripcion.NoEnviar)
                        {
                            continue;
                        }
                        else if (susc.FilaSuscripcion.Periodicidad == (short)PeriodicidadSuscripcion.Diaria)
                        {
                            fecha = fechaActual.AddDays(-1); ;
                        }
                        else if (susc.FilaSuscripcion.Periodicidad == (short)PeriodicidadSuscripcion.Semanal)
                        {
                            switch (fechaActual.DayOfWeek)
                            {
                                case DayOfWeek.Monday:
                                    fecha = fechaActual;
                                    break;
                                case DayOfWeek.Tuesday:
                                    fecha = fechaActual.AddDays(-1);
                                    break;
                                case DayOfWeek.Wednesday:
                                    fecha = fechaActual.AddDays(-2);
                                    break;
                                case DayOfWeek.Thursday:
                                    fecha = fechaActual.AddDays(-3);
                                    break;
                                case DayOfWeek.Friday:
                                    fecha = fechaActual.AddDays(-4);
                                    break;
                                case DayOfWeek.Saturday:
                                    fecha = fechaActual.AddDays(-5);
                                    break;
                                case DayOfWeek.Sunday:
                                    fecha = fechaActual.AddDays(-6);
                                    break;
                            }
                        }

                        if (listaProyectosConNotificacionesActivas.Contains(((Es.Riam.Gnoss.AD.EntityModel.Models.Suscripcion.SuscripcionTesauroProyecto)susc.FilaRelacion).ProyectoID))
                        {
                            GestionIdentidades gestIdent = new GestionIdentidades(identCN.ObtenerIdentidadPorIDCargaLigeraTablas(susc.FilaSuscripcion.IdentidadID), gestPers, gestOrg, loggingService, entityContext, mConfigService, servicesUtilVirtuosoAndReplication);
                            Identidad identidad = gestIdent.ListaIdentidades[susc.FilaSuscripcion.IdentidadID];

                            if (identidad != null)
                            {
                                LogStatus estadoProcesoBoletin = GenerarBoletinIdentidad(identidad, susc, fecha, entityContext, loggingService, redisCacheWrapper, virtuosoAD, servicesUtilVirtuosoAndReplication);
                                if (estadoProcesoBoletin == LogStatus.Error)
                                {
                                    estadoProceso = LogStatus.Error;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    estadoProceso = LogStatus.Error;
                    loggingService.GuardarLog("\r\n\tError al generar el boletín para el perfil '" + perfilID.ToString() + "'.\r\n\tError:" + ex.Message + "\r\n\tTraza:" + ex.StackTrace);

                    continue;
                }
            }
            persCN.Dispose();
            orgCN.Dispose();
            identCN.Dispose();

            return estadoProceso;
        }

        private LogStatus GenerarBoletinIdentidad(Identidad pIdentidad, Suscripcion pSuscripcion, DateTime pFecha, EntityContext entityContext, LoggingService loggingService, RedisCacheWrapper redisCacheWrapper, VirtuosoAD virtuosoAD, IServicesUtilVirtuosoAndReplication servicesUtilVirtuosoAndReplication)
        {
            try
            {
                UtilIdiomas utilIdiomas = new UtilIdiomas(pIdentidad.Persona.FilaPersona.Idioma, pIdentidad.Clave, loggingService, entityContext, mConfigService);

                //comprobar si una SUSCRIPCIÓN tiene boletin generado a partir de una fecha
                SuscripcionCN suscripcionCN = new SuscripcionCN(entityContext, loggingService, mConfigService, servicesUtilVirtuosoAndReplication);
                bool tieneBoletinGenerado = suscripcionCN.TienePerfilBoletinPosteriorAFecha(pSuscripcion.Clave, pFecha);

                if (tieneBoletinGenerado)
                {
                    return LogStatus.NoGenerado;
                }

                //Obtenemos cual es el ultimo store que se ha enviado
                int ultimoScore = suscripcionCN.UltimoScoreSuscripcionEnviado(pSuscripcion.Clave);

                suscripcionCN.Dispose();

                LiveUsuariosCL liveUsuariosCL = new LiveUsuariosCL(entityContext, loggingService, redisCacheWrapper, mConfigService, servicesUtilVirtuosoAndReplication);
                liveUsuariosCL.Dominio = mDominio;

                List<object> listaResultadosLive = new List<object>();

                //Obtenemos de redis las ultimas suscripciones desde el ultimo store enviado
                //Si no se ha enviado ninguno, obtenemos las 10 ultmas suscripciones
                int score = liveUsuariosCL.ObtenerLiveProyectoUsuarioSuscripcionesPorScore(pIdentidad.Persona.UsuarioID, ((Es.Riam.Gnoss.AD.EntityModel.Models.Suscripcion.SuscripcionTesauroProyecto)pSuscripcion.FilaRelacion).ProyectoID, pIdentidad.Persona.FilaPersona.Idioma, ultimoScore, 100, listaResultadosLive);

                //Enviamos las ultimas suscripciones obtenidas
                string resultados = MontarResultadosSuscr(listaResultadosLive, utilIdiomas, pIdentidad, entityContext, loggingService, redisCacheWrapper, virtuosoAD, servicesUtilVirtuosoAndReplication);

                if (!string.IsNullOrEmpty(resultados))
                {
                    //Guardamos la fecha de envio del boletin
                    pSuscripcion.FilaSuscripcion.UltimoEnvio = DateTime.Now;
                    //Guardamos el ultimo score que enviamos
                    pSuscripcion.FilaSuscripcion.ScoreUltimoEnvio = score;
                    //TODO Javier ¿se ha borrado en .net 5?
                    //new ActualizarConjuntoCN().GuardarDatosSuscripcion(pSuscripcion.GestorSuscripcion.SuscripcionDW, null);

                    GestionNotificaciones gestorNot = new GestionNotificaciones(new DataWrapperNotificacion(), loggingService, entityContext, mConfigService, servicesUtilVirtuosoAndReplication);
                    Guid proyectoID = ((Es.Riam.Gnoss.AD.EntityModel.Models.Suscripcion.SuscripcionTesauroProyecto)pSuscripcion.FilaRelacion).ProyectoID;

                    ProyectoCL proyectoCL = new ProyectoCL(entityContext, loggingService, redisCacheWrapper, mConfigService, virtuosoAD, servicesUtilVirtuosoAndReplication);
                    string nombreCorto = proyectoCL.ObtenerNombreCortoProyecto(proyectoID);

                    gestorNot.AgregarNotificacionBoletinSuscripcion(pIdentidad.Persona.Clave, pIdentidad.OrganizacionID, resultados, pIdentidad.Persona.NombreConApellidos, pIdentidad.Email, null, pIdentidad.FilaIdentidad.ProyectoID, nombreCorto, pIdentidad.Persona.FilaPersona.Idioma);

                    NotificacionCN notCN = new NotificacionCN(entityContext, loggingService, mConfigService, servicesUtilVirtuosoAndReplication);
                    notCN.ActualizarNotificacion();
                    notCN.Dispose();

                    return LogStatus.Correcto;
                }
            }
            catch (Exception ex)
            {
                loggingService.GuardarLog("\r\n\tError al generar el boletín para la identidad '" + pIdentidad.Clave.ToString() + "'.\r\n\tError:" + ex.Message + "\r\n\tTraza:" + ex.StackTrace);

                return LogStatus.Error;
            }

            return LogStatus.NoGenerado;
        }


        /// <summary>
        /// Obtiene la identidad actual del usuario para la url-semantica, vacio si estas en una comunidad y "/" si estas en modo personal
        /// </summary>
        public string UrlPerfil(Identidad pIdentidad, UtilIdiomas pUtilidiomas)
        {
            string urlPerfil = "/";

            try
            {
                if (pIdentidad != null && (pIdentidad.TrabajaConOrganizacion || pIdentidad.EsIdentidadProfesor))
                {
                    string nombreCorto = pIdentidad.PerfilUsuario.NombreCortoOrg;

                    if ((pIdentidad.EsIdentidadProfesor) && (string.IsNullOrEmpty(pIdentidad.PerfilUsuario.NombreCortoOrg)))
                    {
                        nombreCorto = pIdentidad.PerfilUsuario.NombreCortoUsu;
                    }

                    urlPerfil += pUtilidiomas.GetText("URLSEM", "IDENTIDAD") + "/" + nombreCorto + "/";
                }
            }
            catch (Exception ex)
            {
            }
            return urlPerfil;

        }

        /// <summary>
        /// Determina el texto de la entrada que tendra una operacion de envio de notificaciones
        /// </summary>
        /// <param name="pStatus">Estado de la operacion del envio</param>
        /// <returns></returns>
        private String CrearEntradaRegistro(LogStatus pEstado, String pDetalles)
        {
            String entradaLog = String.Empty;

            switch (pEstado)
            {
                case LogStatus.Correcto:
                    entradaLog = "\r\n\t >> OK: ";
                    break;
                case LogStatus.Error:
                    entradaLog = "\r\n\t >> ALERT: ";
                    break;
                case LogStatus.NoGenerado:
                    entradaLog = "\r\n\t >> OK: ";
                    break;
            }
            return entradaLog + pDetalles;
        }

        /// <summary>
        /// Carga las notificaciones que deben enviarse
        /// </summary>
        private void CargarPerfilesConSuscripcion(LoggingService loggingService, EntityContext entityContext, IServicesUtilVirtuosoAndReplication servicesUtilVirtuosoAndReplication)
        {
            bool DatosCargados = false;
            int NumeroIntentosCarga = 0;
            bool cargarPerfilesTodosProyesctos = false;

            if (/*ParametroAplicacionDS != null && ParametroAplicacionDS.ParametroAplicacion != null &&*/ GestorParametroAplicacionDS.ParametroAplicacion.Count > 0)
            {
                List<ParametroAplicacion> filasParam = GestorParametroAplicacionDS.ParametroAplicacion.Where(paramatroApp => paramatroApp.Parametro.Equals("EnviarNotificacionesDeSuscripciones")).ToList();// ("Parametro='EnviarNotificacionesDeSuscripciones'");
                if (filasParam != null && filasParam.Count > 0)
                {
                    if (filasParam[0].Valor.ToLower().Equals("true"))
                    {
                        cargarPerfilesTodosProyesctos = true;
                    }
                }
            }

            while (!DatosCargados && NumeroIntentosCarga <= 5)
            {
                ComprobarCancelacionHilo();

                try
                {
                    NumeroIntentosCarga++;

                    IdentidadCN identCN = new IdentidadCN(entityContext, loggingService, mConfigService, servicesUtilVirtuosoAndReplication);

                    if (cargarPerfilesTodosProyesctos)
                    {
                        mListaPerfiles = identCN.ObtenerListaPerfilesIDConSuscripcion();
                    }
                    else
                    {
                        short diaSemana = (short)DateTime.Today.DayOfWeek;
                        if (diaSemana.Equals((short)DayOfWeek.Sunday))
                        {
                            //el Domingo en la enumeración es el 0 y BD será el 7, siendo el 1 el Lunes en BD
                            diaSemana = 7;
                        }
                        mListaPerfiles = identCN.ObtenerListaPerfilesIDConSuscripcionPorProyectos(diaSemana);
                    }

                    identCN.Dispose();
                    DatosCargados = true;
                }
                catch (Exception)
                {
                    int intentosRestantes = 5 - NumeroIntentosCarga;
                    loggingService.GuardarLog("Se han producido errores al intentar cargar datos. Puede producirse si el servidor sql server está inactivo.\r\n\t Se volverá a intentar pasados 1:30 minutos.(QUEDAN " + intentosRestantes.ToString() + " INTENTOS)");

                    Thread.Sleep(90000);
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="pSuscripcion"></param>
        /// <param name="resDS"></param>
        /// <param name="pGestorIdentAutores"></param>
        /// <param name="pUtilIdiomas"></param>
        /// <param name="pIdentidadSuscripcion"></param>
        /// <returns></returns>
        private string MontarResultadosSuscr(List<object> pListaSuscripciones, UtilIdiomas pUtilIdiomas, Identidad pIdentidadSuscripcion, EntityContext entityContext, LoggingService loggingService, RedisCacheWrapper redisCacheWrapper, VirtuosoAD virtuosoAD, IServicesUtilVirtuosoAndReplication servicesUtilVirtuosoAndReplication)
        {
            string UrlPerfil = "/";

            if (pIdentidadSuscripcion != null && pIdentidadSuscripcion.TrabajaConOrganizacion)
            {
                UrlPerfil += pUtilIdiomas.GetText("URLSEM", "IDENTIDAD") + "/" + pIdentidadSuscripcion.PerfilUsuario.NombreCortoOrg + "/";
            }
            string mensaje = "";

            if (pListaSuscripciones.Count == 0)
            {
                return "";
            }

            string UrlIntraGnoss = "";
            ParametroAplicacionCN aplicacionCN = new ParametroAplicacionCN(entityContext, loggingService, mConfigService, servicesUtilVirtuosoAndReplication);
            UrlIntraGnoss = aplicacionCN.ObtenerUrl();
            aplicacionCN.Dispose();
            bool incluirSeparador = false;

            foreach (object suscripcion in pListaSuscripciones)
            {
                string[] parametros = suscripcion.ToString().Replace("_leido", "").Split('_');

                short tipo = short.Parse(parametros[0]);
                Guid elementoID = new Guid(parametros[1]);
                Guid proyID = new Guid(parametros[2]);
                string idioma = parametros[parametros.Length - 1];
                string infoExtra = "";
                if (parametros.Length > 4)
                {
                    infoExtra = parametros[3];
                }

                if (tipo == (short)TipoLive.Recurso || tipo == (short)TipoLive.Debate || tipo == (short)TipoLive.Pregunta)
                {
                    DocumentacionCN docCN = new DocumentacionCN(entityContext, loggingService, mConfigService, servicesUtilVirtuosoAndReplication);
                    GestorDocumental gestDoc = new GestorDocumental(docCN.ObtenerDocumentoPorID(elementoID), loggingService, entityContext);
                    docCN.Dispose();
                    Documento doc = gestDoc.ListaDocumentos[elementoID];

                    ProyectoCN proyCN = new ProyectoCN(entityContext, loggingService, mConfigService, servicesUtilVirtuosoAndReplication);
                    GestionProyecto gestProy = new GestionProyecto(proyCN.ObtenerProyectoCargaLigeraPorID(proyID), loggingService, entityContext);

                    Proyecto proy = gestProy.ListaProyectos[proyID];

                    if (incluirSeparador)
                    {
                        mensaje += "<div style=\"border-top: 1px solid rgb(204, 204, 204); height: 20px;\"></div><br>";
                    }
                    string urlBase = UrlIntraGnoss.Substring(0, UrlIntraGnoss.Length - 1);

                    if (pUtilIdiomas.LanguageCode != "es" && pUtilIdiomas.LanguageCode != "es-es")
                    {
                        urlBase = urlBase + "/" + pUtilIdiomas.LanguageCode;
                    }

                    string urlPropiaComunidad = proyCN.ObtenerURLPropiaProyectoPorNombreCorto(proy.NombreCorto);
                    proyCN.Dispose();

                    GnossUrlsSemanticas gnossUrlsSemanticas = new GnossUrlsSemanticas(loggingService, entityContext, mConfigService);
                    string urlDocumento = gnossUrlsSemanticas.GetURLBaseRecursosFicha(urlBase, pUtilIdiomas, proy.NombreCorto, UrlPerfil, doc, false);

                    //Titulo del resultado
                    mensaje += "<span style=\"color: rgb(82, 132, 173); font-weight: bold; font-size: 15px; margin-top: 4px;\"><a style=\"color: rgb(82, 132, 173); text-decoration: none;\" href=\"" + urlDocumento + "\">" + doc.Titulo + "</a></span><br>";

                    ProyectoCL proyectoCL = new ProyectoCL(entityContext, loggingService, redisCacheWrapper, mConfigService, virtuosoAD, servicesUtilVirtuosoAndReplication);
                    Dictionary<string, string> parametroProyecto = proyectoCL.ObtenerParametrosProyecto(proyID);
                    proyectoCL.Dispose();

                    int numCaracteresDescripcion = 500;
                    if (parametroProyecto != null && parametroProyecto.ContainsKey(ParametroAD.NumeroCaracteresDescripcion))
                    {
                        int aux = 0;
                        if (int.TryParse(parametroProyecto[ParametroAD.NumeroCaracteresDescripcion], out aux))
                        {
                            numCaracteresDescripcion = aux;
                        }
                    }

                    //Descripcion del resultado
                    mensaje += UtilCadenas.AcortarDescripcionHtml(doc.Descripcion, numCaracteresDescripcion);

                    if (doc.Categorias.Count > 0)
                    {
                        mensaje += "<p><small>" + pUtilIdiomas.GetText("LISTARECURSOS", "CATEGORIAS");
                        bool ponerComa = false;

                        foreach (CategoriaTesauro catTesauro in doc.Categorias.Values)
                        {
                            if (ponerComa)
                            {
                                mensaje += ", ";
                            }
                            string urlCategoria = gnossUrlsSemanticas.GetURLBaseRecursosCategoriaDocumentoConIDs(urlBase, pUtilIdiomas, proy.NombreCorto, UrlPerfil, false, catTesauro.NombreSem[pUtilIdiomas.LanguageCode], catTesauro.Clave, pUtilIdiomas.GetText("URLSEM", "BUSQUEDAAVANZADA"));
                            mensaje += "<a style=\"color: rgb(82, 132, 173); text-decoration: none;\" href=\"" + urlCategoria + "\">" + catTesauro.Nombre[pUtilIdiomas.LanguageCode] + "</a>";
                            ponerComa = true;
                        }
                        mensaje += "</small></p>";
                    }

                    mensaje += "<p><small>" + pUtilIdiomas.GetText("SUSCRIPCIONES", "RECURSOPUBLICADOEL", doc.Fecha.ToString("dd.MM.yy")) + "</small></p>";

                    incluirSeparador = true;
                }
            }

            return mensaje;
        }

        #endregion

        #region Propiedades

        /// <summary>
        /// 
        /// </summary>
        public string NombreBD
        {
            get
            {
                return mFicheroConfiguracionBD;
            }
        }

        #endregion
    }
}
