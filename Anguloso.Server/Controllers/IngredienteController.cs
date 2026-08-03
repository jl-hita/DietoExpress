using Anguloso.Server.Logica;
using Anguloso.Server.Model;
using Anguloso.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Net.NetworkInformation;

//namespace Anguloso.Server.Controllers;

//[Authorize]
//[Route("api/[controller]")]
//[ApiController]
//public class IngredienteController : Controller
//{
//    private readonly angulosodbContext _context;

//    public IngredienteController(angulosodbContext context)
//    {
//        _context = context;
//    }

//    [HttpPut("addIngrediente")] 
//    public BoolMensaje AddIngrediente([FromBody] ingredientes ing)
//    {
//        try
//        {
//            //Obtenemos el nombre de usuario
//            string? userName = User.Identity?.Name ?? null;

//            Console.WriteLine($"Usuario -> {userName}");

//            string? userName2 = User.Claims.FirstOrDefault(c => c.Type == "unique_name")?.Value;
//            Console.WriteLine($"Usuario manual: {userName2}");

//            var authHeader = HttpContext.Request.Headers["Authorization"].FirstOrDefault();
//            Console.WriteLine($"Header Authorization: {authHeader}");

//            if (string.IsNullOrEmpty(userName))
//            {
//                return new BoolMensaje
//                {
//                    Exito = false,
//                    Mensaje = "Usuario no encontrado -> ¿logueado?"
//                };
//            }

//            //Buscamos la id del usuario
//            users? usuario = _context.users.AsNoTracking().FirstOrDefault(u => u.username == userName);

//            if(usuario == null)
//            {
//                return new BoolMensaje
//                {
//                    Exito = false,
//                    Mensaje = "Usuario no encontrado -> ¿logueado?"
//                };
//            }

//            //Buscamos el ingrediente a ver si ya existe uno con el mismo nombre y misma unidad y luego comprobamos que no haya sido comprado
//            ingredientes? ingrediente = _context.ingredientes.FirstOrDefault(i => i.id_usuario == usuario.id && i.nombre == ing.nombre && i.unidad == ing.unidad);

//            //Si no existe creamos un nuevo ingrediente y lo guardamos en la bbdd
//            if( ingrediente == null )
//            {
//                ingrediente = new();
//                ingrediente.id_usuario = usuario.id;
//                ingrediente.nombre = ing.nombre;
//                ingrediente.descripcion = ing.descripcion;
//                ingrediente.cantidad = ing.cantidad;
//                ingrediente.unidad = ing.unidad;
//                ingrediente.comprado = false;
//                _context.ingredientes.Add(ingrediente);
//            }

//            //Si existe y está como comprado ponemos comprado a false y la cantidad total es la pasada
//            else if(ingrediente.comprado)
//            {
//                ingrediente.comprado = false;
//                ingrediente.cantidad = ing.cantidad;
                
//            }

//            //Si existe y no está como comprado se suma la cantidad pasada a la total
//            else
//            {
//                ingrediente.cantidad += ing.cantidad;
//            }

//            //Guardamos cambios a la BBDD y return
//            _context.SaveChanges();
//            return new BoolMensaje
//            {
//                Exito = true,
//                Mensaje = $"Ingrediente {ingrediente.nombre} guardado correctamente"
//            };
//        }
//        catch (Exception e)
//        {
//            return new BoolMensaje
//            {
//                Exito = false,
//                Mensaje = $"Error guardando ingrediente -> {e.Message}"
//            };
//        }
//    }

//    [HttpGet("getIngredientes")]
//    public List<ingredientes> GetIngredientes()
//    {
//        List<ingredientes> listaIngredientes = new();

//        //Obtenemos el nombre de usuario
//        string ? userName = User.Identity?.Name ?? null;

//        if (string.IsNullOrEmpty(userName))
//        {
//            return listaIngredientes;
//        }

//        //Buscamos la id del usuario
//        users? usuario = _context.users.AsNoTracking().FirstOrDefault(u => u.username == userName);

//        if (usuario == null)
//        {
//            return listaIngredientes;
//        }

//        //Buscamos el ingrediente a ver si ya existe uno con el mismo nombre y misma unidad y luego comprobamos que no haya sido comprado
//        List<ingredientes>? ingredientes = _context.ingredientes.AsNoTracking().Where(i => i.id_usuario == usuario.id).ToList();
        
//        if(ingredientes != null)
//        {
//            return ingredientes;
//        }

//        return listaIngredientes;
//    }

//    [HttpPut("delIngredientes")]
//    public BoolMensaje DelIngrediente([FromBody] ingredientes ing)
//    {
//        try
//        {
//            //Obtenemos el nombre de usuario
//            string? userName = User.Identity?.Name ?? null;

//            if (string.IsNullOrEmpty(userName))
//            {
//                return new BoolMensaje
//                {
//                    Exito = false,
//                    Mensaje = "Usuario no encontrado -> ¿logueado?"
//                };
//            }

//            //Buscamos la id del usuario
//            users? usuario = _context.users.AsNoTracking().FirstOrDefault(u => u.username == userName);

//            if (usuario == null)
//            {
//                return new BoolMensaje
//                {
//                    Exito = false,
//                    Mensaje = "Usuario no encontrado -> ¿logueado?"
//                };
//            }

//            //Buscamos el ingrediente con el mismo nombre y misma unidad
//            ingredientes? ingrediente = _context.ingredientes.FirstOrDefault(i => i.id_usuario == usuario.id && i.nombre == ing.nombre && i.unidad == ing.unidad);

//            if(ingrediente == null)
//            {
//                return new BoolMensaje
//                {
//                    Exito = false,
//                    Mensaje = $"Ingrediente {ing.nombre} no encontrado"
//                };
//            }

//            _context.Remove(ingrediente);
//            _context.SaveChanges();
//            return new BoolMensaje
//            {
//                Exito = false,
//                Mensaje = $"Ingrediente {ing.nombre} borrado"
//            };
//        }
//        catch (Exception e)
//        {
//            return new BoolMensaje
//            {
//                Exito = false,
//                Mensaje = $"Error guardando ingrediente -> {e.Message}"
//            };
//        }
//    }

//    [HttpPut("marcarCompradoIngredientes")]
//    public BoolMensaje MarcarCompradoIngrediente([FromBody] ingredientes ing)
//    {
//        try
//        {
//            //Obtenemos el nombre de usuario
//            string? userName = User.Identity?.Name ?? null;

//            if (string.IsNullOrEmpty(userName))
//            {
//                return new BoolMensaje
//                {
//                    Exito = false,
//                    Mensaje = "Usuario no encontrado -> ¿logueado?"
//                };
//            }

//            //Buscamos la id del usuario
//            users? usuario = _context.users.AsNoTracking().FirstOrDefault(u => u.username == userName);

//            if (usuario == null)
//            {
//                return new BoolMensaje
//                {
//                    Exito = false,
//                    Mensaje = "Usuario no encontrado -> ¿logueado?"
//                };
//            }

//            //Buscamos el ingrediente con el mismo nombre y misma unidad
//            ingredientes? ingrediente = _context.ingredientes.FirstOrDefault(i => i.id_usuario == usuario.id && i.nombre == ing.nombre && i.unidad == ing.unidad);

//            if (ingrediente == null)
//            {
//                return new BoolMensaje
//                {
//                    Exito = false,
//                    Mensaje = $"Ingrediente {ing.nombre} no encontrado"
//                };
//            }

//            ingrediente.comprado = ing.comprado;
//            _context.SaveChanges();
//            return new BoolMensaje
//            {
//                Exito = false,
//                Mensaje = $"Ingrediente {ing.nombre} borrado"
//            };
//        }
//        catch (Exception e)
//        {
//            return new BoolMensaje
//            {
//                Exito = false,
//                Mensaje = $"Error guardando ingrediente -> {e.Message}"
//            };
//        }
//    }
//}