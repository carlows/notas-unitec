﻿using IdentitySample.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using control_notas_cit.Models.Repositorios;
using Microsoft.AspNet.Identity;
using Microsoft.AspNet.Identity.Owin;
using Microsoft.AspNet.Identity.EntityFramework;
using control_notas_cit.Models.ViewModels;
using control_notas_cit.Models.Entidades;
using System.IO;
using LinqToExcel;
using control_notas_cit.Helpers;
using control_notas_cit.Models.Servicios;

namespace control_notas_cit.Controllers
{
    [Authorize(Roles = "Coordinador")]
    public class CoordinadorController : BaseController
    {
        private readonly IUnitOfWork _uow;

        public CoordinadorController()
        {
            // Obtengo el contexto que OWIN creó al iniciar la aplicación
            var AppContext = ApplicationDbContext.GetDBContext();
            _uow = new UnitOfWork(AppContext);            
        }

        //
        // GET: /Coordinador/
        public ActionResult Index()
        {
            CoordinadorIndexViewModel model = new CoordinadorIndexViewModel();

            var currentUser = GetCurrentUser();

            if (currentUser == null)
            {
                return RedirectToAction("Logoff", "Account");
            }

            if (currentUser.Celula == null)
            {
                return RedirectToAction("Logoff", "Account");
            }

            if (currentUser.Proyecto == null)
            {
                return RedirectToAction("Logoff", "Account");
            }

            model.Celula = GetCurrentCelula();
            model.Semana = GetCurrentSemana();
            model.MinutaSemana = GetCurrentMinuta();

            List<Asistencia> asistenciasCelulaCurrentSemana = null;

            if (model.Semana != null)
            {
                asistenciasCelulaCurrentSemana = model.Semana.Asistencias.Where(a => a.Celula.CelulaID == model.Celula.CelulaID && a.Semana.SemanaID == model.Semana.SemanaID).ToList();
            }

            if (asistenciasCelulaCurrentSemana != null && asistenciasCelulaCurrentSemana.Count > 0)
            {
                model.AsistenciaEnviada = true;
            }
            else
            {
                model.AsistenciaEnviada = false;
            }

            return View(model);
        }

        //
        // GET: /Coordinador/EditarCelula/
        public ActionResult EditarCelula()
        {
            var celula = GetCurrentCelula();

            if (celula == null)
            {
                TempData["message"] = "No se pudo encontrar la celula";
                return RedirectToAction("Index");
            }

            var model = new CelulaEditViewModel()
            {
                Id = celula.CelulaID,
                Nombre = celula.Nombre,
                Descripcion = celula.Descripcion
            };

            return View(model);
        }

        //
        // POST: /Coordinador/EditarCelula/
        [HttpPost]
        public ActionResult EditarCelula(CelulaEditViewModel model)
        {
            if (ModelState.IsValid)
            {
                var celula = _uow.RepositorioCelulas.SelectById(model.Id);

                if (celula == null)
                {
                    ModelState.AddModelError("", "No se pudo encontrar la celula");
                    return View(model);
                }

                celula.Nombre = model.Nombre;
                celula.Descripcion = model.Descripcion;

                _uow.RepositorioCelulas.Update(celula);
                _uow.Save();

                return RedirectToAction("Index");
            }

            return View(model);
        }

        //
        // GET: /Coordinador/ListaAlumnos/
        public ActionResult ListaAlumnos()
        {
            var model = GetCurrentCelula().Alumnos.OrderBy(a => a.Nombre).ThenBy(a => a.Apellido).ToList();

            return View(model);
        }

        //
        // GET: /Coordinador/AgregarAlumno/
        public ActionResult AgregarAlumno()
        {
            var calendario = GetCurrentCalendario();

            if ((bool)calendario.Finalizado || (bool)calendario.IsLastWeek)
            {
                TempData["message"] = "Debes iniciar un nuevo calendario para agregar alumnos";
                return RedirectToAction("ListaAlumnos");
            }

            return View();
        }

        //
        // POST: /Coordinador/AgregarAlumno/
        [HttpPost]
        public ActionResult AgregarAlumno(AlumnoViewModel model)
        {
            if (ModelState.IsValid)
            {
                var proyecto = GetCurrentProyecto();
                var celula = GetCurrentCelula();
                var alumnoExistente = proyecto.Celulas
                    .SelectMany(c => c.Alumnos)
                    .Where(a => a.Cedula == model.Cedula)
                    .SingleOrDefault();

                if (alumnoExistente == null)
                {
                    Alumno alumno = new Alumno()
                    {
                        Nombre = model.Nombre,
                        Apellido = model.Apellido,
                        Cedula = ModelHelpers.LimpiarPuntos(model.Cedula),
                        Telefono = model.Telefono,
                        Email = model.Email,
                        Celula = GetCurrentCelula()
                    };

                    _uow.RepositorioAlumnos.Insert(alumno);
                    _uow.Save();
                }
                else
                {
                    TempData["message"] = "Cedula duplicada, no se agregó el alumno.";
                }

                return RedirectToAction("ListaAlumnos");
            }

            return View(model);
        }

        public ActionResult AgregarAlumnos()
        {
            var calendario = GetCurrentCalendario();

            if ((bool)calendario.Finalizado || (bool)calendario.IsLastWeek)
            {
                TempData["message"] = "Debes iniciar un nuevo calendario para agregar alumnos";
                return RedirectToAction("ListaAlumnos");
            }

            return View();
        }

        [HttpPost]
        public ActionResult AgregarAlumnos(HttpPostedFileBase file)
        {
            if (file != null && file.ContentLength > 0)
            {
                string extension = Path.GetExtension(file.FileName);
                if (extension.Equals(".xls") || extension.Equals(".xlsx") || extension.Equals(".csv"))
                {
                    try
                    {
                        // upload the file
                        string fileName = string.Format("Excel-{0:dd-MM-yyyy-HH-mm-ss}{1}", DateTime.Now, extension);
                        string path = Path.Combine(Server.MapPath("~/ExcelTemp"), fileName);
                        file.SaveAs(path);

                        // get the file
                        var excel = new ExcelQueryFactory(path);
                        var alumnos = excel.Worksheet<AlumnoViewModel>().ToList();

                        // delete the file
                        if (System.IO.File.Exists(path))
                        {
                            System.IO.File.Delete(path);
                        }

                        var proyecto = GetCurrentProyecto();
                        var celula = GetCurrentCelula();
                        var calendario = GetCurrentCalendario();

                        // send or do something with data
                        foreach(AlumnoViewModel al in alumnos)
                        {
                            var alumnoExistente = proyecto.Celulas
                                .SelectMany(c => c.Alumnos)
                                .Where(a => a.Cedula == al.Cedula)
                                .SingleOrDefault();

                            if (alumnoExistente == null)
                            {
                                Alumno alumno = new Alumno
                                {
                                    Nombre = al.Nombre,
                                    Apellido = al.Apellido,
                                    Cedula = ModelHelpers.LimpiarPuntos(al.Cedula),
                                    Email = al.Email,
                                    Telefono = al.Telefono,
                                    Celula = celula
                                };

                                int numeroSemanaActual = calendario.Semanas.Where(s => s.SemanaID == calendario.SemanaActualID).Select(c => c.NumeroSemana).SingleOrDefault();
                                for (int index = 1; index <= (numeroSemanaActual - 1); index++)
                                {
                                    var semana = calendario.Semanas.Where(s => s.NumeroSemana == index).SingleOrDefault();

                                    Asistencia asistencia = new Asistencia()
                                    {
                                        Alumno = alumno,
                                        Semana = semana,
                                        Celula = celula
                                    };

                                    _uow.RepositorioAsistencias.Insert(asistencia);
                                }

                                _uow.RepositorioAlumnos.Insert(alumno);
                            }
                            else
                            {
                                TempData["message"] = "Algunos alumnos no fueron agregados, datos repetidos";
                            }
                        }

                        _uow.Save();

                        return RedirectToAction("ListaAlumnos");
                    }
                    catch (Exception ex)
                    {
                        ViewBag.Message = "ERROR:" + ex.Message.ToString();
                    }
                }
            }
            else
            {
                ViewBag.Message = "Debes especificar algún archivo.";
            }

            return View();
        }

        //
        // GET: /Coordinador/EditarAlumno/1
        public ActionResult EditarAlumno(int? id)
        {
            if (id == null)
            {
                TempData["message"] = "No se pudo encontrar el alumno";
                return RedirectToAction("ListaAlumnos");
            }

            var alumno = _uow.RepositorioAlumnos.SelectById(id);

            if (alumno == null)
            {
                TempData["message"] = "No se pudo encontrar el alumno";
                return RedirectToAction("ListaAlumnos");
            }

            var model = new AlumnoViewModel()
            {
                Id = alumno.AlumnoID,
                Nombre = alumno.Nombre,
                Apellido = alumno.Apellido,
                Telefono = alumno.Telefono,
                Cedula = alumno.Cedula,
                Email = alumno.Email
            };

            return View(model);
        }

        //
        // POST: /Coordinador/EditarAlumno/
        [HttpPost]
        public ActionResult EditarAlumno(AlumnoViewModel model)
        {
            if (ModelState.IsValid)
            {
                Alumno alumno = _uow.RepositorioAlumnos.SelectById(model.Id);

                if (alumno == null)
                {
                    ModelState.AddModelError("", "No se pudo encontrar el alumno");
                    return View(model);
                }

                alumno.Nombre = model.Nombre;
                alumno.Apellido = model.Apellido;
                alumno.Cedula = model.Cedula;
                alumno.Telefono = model.Telefono;
                alumno.Email = model.Email;

                _uow.RepositorioAlumnos.Update(alumno);
                _uow.Save();

                return RedirectToAction("ListaAlumnos");
            }

            return View(model);
        }

        // POST: /Coordinador/BorrarAlumno/1
        [HttpPost]
        public ActionResult BorrarAlumno(int? id)
        {
            if (id == null)
            {
                TempData["message"] = "No se pudo encontrar el alumno";
                return RedirectToAction("ListaAlumnos");
            }

            _uow.RepositorioAlumnos.Delete(id);
            _uow.Save();

            return RedirectToAction("ListaAlumnos");
        }

        //
        // GET: /Coordinador/AgregarMinuta/
        public ActionResult AgregarMinuta()
        {
            if (GetCurrentSemana() == null)
            {
                TempData["message"] = "No se pudo encontrar la semana";
                return RedirectToAction("Index");
            }

            if (GetCurrentCalendario().IsLastWeek == true)
            {
                return RedirectToAction("Index");
            }

            var minutaActual = GetCurrentMinuta();
            if (minutaActual != null)
            {
                if (minutaActual.Aprobada == true)
                {
                    TempData["message"] = "La minuta de esta semana ya fué enviada y aprobada, no se puede editar.";
                    return RedirectToAction("Index");
                }

                return View(new MinutaCelulaViewModel()
                {
                    Id = minutaActual.MinutaID,
                    Contenido = minutaActual.Contenido
                });
            }

            return View(new MinutaCelulaViewModel());
        }

        //
        // POST: /Coordinador/AgregarMinuta/
        [HttpPost]
        public ActionResult AgregarMinuta(MinutaCelulaViewModel model, HttpPostedFileBase archivo)
        {
            if (ModelState.IsValid)
            {
                var celula = GetCurrentCelula();
                var semana = GetCurrentSemana();

                if (celula == null || semana == null)
                {
                    ModelState.AddModelError("", "No se pudo encontrar la celula o la semana");
                    return View(model);
                }

                if (archivo != null)
                {
                    if (archivo.ContentLength > 0)
                    {
                        if (Path.GetExtension(archivo.FileName) == ".txt")
                        {
                            BinaryReader b = new BinaryReader(archivo.InputStream);
                            byte[] binData = b.ReadBytes(archivo.ContentLength);

                            model.Contenido = System.Text.Encoding.UTF8.GetString(binData);
                        }
                    }
                    else
                    {
                        ModelState.AddModelError("", "La minuta no puede estar vacia.");
                        return View();
                    }
                }
                else if(model.Contenido == null)
                {
                    ModelState.AddModelError("", "La minuta no puede estar vacia.");
                    return View();
                }

                Minuta minuta = new Minuta()
                {
                    Contenido = model.Contenido,
                    Celula = celula,
                    Semana = semana
                };

                if (model.Id == 0)
                {
                    _uow.RepositorioMinutas.Insert(minuta);
                    _uow.Save();
                }
                else
                {
                    minuta.MinutaID = model.Id;

                    _uow.RepositorioMinutas.Update(minuta);
                    _uow.Save();
                }

                return RedirectToAction("Index");
            }

            return View(model);
        }

        //
        // GET: /Coordinador/AsistenciaSemana/
        public ActionResult AsistenciaSemana()
        {
            var semana = GetCurrentSemana();
            var celula = GetCurrentCelula();
            var islastweek = GetCurrentCalendario().IsLastWeek;

            if (semana == null || celula == null || islastweek == true)
            {
                TempData["message"] = "No se pueden enviar asistencias en este momento.";
                return RedirectToAction("Index");
            }

            var asistenciasCelula = semana.Asistencias.Where(a => a.Celula.CelulaID == celula.CelulaID && a.Semana.SemanaID == semana.SemanaID).ToList();

            if (asistenciasCelula.Count > 0)
            {
                TempData["message"] = "No se pueden enviar asistencias en este momento.";
                return RedirectToAction("Index");
            }

            var model = new AsistenciaSemanaViewModel()
            {
                Alumnos = GetCurrentCelula().Alumnos
            };

            return View(model);
        }

        //
        // POST: /Coordinador/AsistenciaSemana/
        [HttpPost]
        public ActionResult AsistenciaSemana(AsistenciaSemanaViewModel model)
        {
            model.Alumnos = GetCurrentCelula().Alumnos;

            if (ModelState.IsValid)
            {
                var celula = GetCurrentCelula();
                var semana = GetCurrentSemana();

                if (semana == null || celula == null)
                {
                    ModelState.AddModelError("", "Hubo un error al encontrar la celula o la semana");
                    return View(model);
                }

                foreach (Alumno alumno in celula.Alumnos)
                {
                    Asistencia asistencia = new Asistencia()
                    {
                        Alumno = alumno,
                        Semana = semana,
                        Celula = celula
                    };

                    if (model.ID_Alumnos.Contains(alumno.AlumnoID))
                    {
                        asistencia.Asistio = true;
                    }

                    _uow.RepositorioAsistencias.Insert(asistencia);
                }

                _uow.Save();

                return RedirectToAction("Index");
            }

            return View(model);
        }

        //
        // GET: /Coordinador/Minutas/
        public ActionResult Minutas()
        {
            return View(GetCurrentCelula().Minutas.Where(m => m.Semana.Calendario.CalendarioID == GetCurrentCalendario().CalendarioID).ToList());
        }

        // Obtiene el usuario logueado actualmente
        private ApplicationUser GetCurrentUser()
        {
            // Tira una excepcion cuando el explorador ya tiene una sesion iniciada, debido a que al ejecutar el Seed, el id es totalmente distinto
            return _uow.RepositorioUsuarios.SelectAll().Where(u => u.Id == User.Identity.GetUserId()).SingleOrDefault();
        }

        // Obtiene la celula
        private Celula GetCurrentCelula()
        {
            return GetCurrentUser().Celula;
        }

        private Proyecto GetCurrentProyecto()
        {
            return GetCurrentUser().Celula.Proyecto;
        }

        // Obtiene el calendario o devuelve null si no existe ninguno
        private Calendario GetCurrentCalendario()
        {
            var celula = GetCurrentCelula();
            return celula.Proyecto.Calendarios.Where(c => c.CalendarioID == celula.Proyecto.CalendarioActualID).SingleOrDefault();
        }

        // Obtiene la semana actual o devuelve null si no existe calendario aun
        private Semana GetCurrentSemana()
        {
            var calendario = GetCurrentCalendario();
            if (calendario == null)
                return null;

            return calendario.Semanas.Where(s => s.SemanaID == calendario.SemanaActualID).SingleOrDefault();
        }

        // Obtiene la minuta actual o devuelve null si no existe calendario aun
        private Minuta GetCurrentMinuta()
        {
            var semana = GetCurrentSemana();
            var celula = GetCurrentCelula();

            return celula.Minutas.Where(m => m.Semana.SemanaID == semana.SemanaID && m.Celula.CelulaID == celula.CelulaID).SingleOrDefault();
        }
    }
}