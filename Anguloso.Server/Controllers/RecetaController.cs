using Anguloso.Server.Model;
using Anguloso.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

/*
namespace Anguloso.Server.Controllers;

//[Authorize]
[Route("api/[controller]")]
[ApiController]
public class RecetaController : Controller
{
    private readonly angulosodbContext _context;

    public RecetaController(angulosodbContext context)
    {
        _context = context;
    }

    [HttpPut("addReceta")]
    public BoolMensaje AddReceta([FromBody] RecetaRequest request)
    {
        try
        {
            //Obtenemos el nombre de usuario
            string? userName = User.Identity?.Name ?? null;

            Console.WriteLine($"Usuario -> {userName}");

            foreach (var claim in User.Claims)
                Console.WriteLine($"{claim.Type} = {claim.Value}");

            Console.WriteLine($"Después de claims");

            if (string.IsNullOrEmpty(userName))
            {
                Console.WriteLine($"Usuario no encontrado -> ¿logueado?");
                return new BoolMensaje
                {
                    Exito = false,
                    Mensaje = "Usuario no encontrado -> ¿logueado?"
                };
            }

            //Buscamos la id del usuario
            users? usuario = _context.users.AsNoTracking().FirstOrDefault(u => u.username == userName);

            if (usuario == null)
            {
                Console.WriteLine($"Usuario no encontrado -> ¿logueado?");
                return new BoolMensaje
                {
                    Exito = false,
                    Mensaje = "Usuario no encontrado -> ¿logueado?"
                };
            }

            //Creamos e insertamos la receta en la BBDD
            var nuevaReceta = new recetas
            {
                nombre = request.Receta.nombre,
                descripcion = request.Receta.descripcion,
                receta = request.Receta.receta,
                fecha_creacion = DateTime.Now,
                id_usuario = usuario.id
            };
            _context.recetas.Add(nuevaReceta);
            _context.SaveChanges();

            //Recuperamos el ID de la receta
            recetas? idReceta = _context.recetas.AsNoTracking().Where(r => r.id_usuario == usuario.id).OrderByDescending(r => r.fecha_creacion).FirstOrDefault();

            if(idReceta == null)
            {
                Console.WriteLine($"Ha ocurrido algún error al guardar la receta");
                return new BoolMensaje
                {
                    Exito = false,
                    Mensaje = "Ha ocurrido algún error al guardar la receta"
                };
            }

            //Guardamos los ingredientes usando el ID de la receta
            foreach(ingredientes ing in request.Ingredientes)
            {
                ingredientes ingrediente = new();
                //ingrediente.id_usuario = usuario.id;
                ingrediente.nombre = ing.nombre;
                ingrediente.descripcion = ing.descripcion;
                ingrediente.cantidad = ing.cantidad;
                ingrediente.unidad = ing.unidad;
                ingrediente.comprado = false;
                ingrediente.id_receta = idReceta.id;
                _context.ingredientes.Add(ingrediente);
            }
            _context.SaveChanges();
            Console.WriteLine($"Receta guardada con éxito");

            return new BoolMensaje
            {
                Exito = true,
                Mensaje = "Receta guardada con éxito"
            };
        }
        catch (Exception e)
        {
            Console.WriteLine($"Error guardando receta -> {e.Message}");
            return new BoolMensaje
            {
                Exito = false,
                Mensaje = $"Error guardando receta -> {e.Message}"
            };
        }
    }

    [Authorize]
    [HttpPut("addRecetaSolo")]
    public BoolMensaje AddRecetaSolo([FromBody] recetas rec)
    {
        try
        {
            //Obtenemos el nombre de usuario
            string? userName = User.Identity?.Name ?? null;

            Console.WriteLine($"Usuario -> {userName}");

            foreach (var claim in User.Claims)
                Console.WriteLine($"{claim.Type} = {claim.Value}");

            Console.WriteLine($"Después de claims");

            if (string.IsNullOrEmpty(userName))
            {
                Console.WriteLine($"Usuario no encontrado -> ¿logueado?");
                return new BoolMensaje
                {
                    Exito = false,
                    Mensaje = "Usuario no encontrado -> ¿logueado?"
                };
            }

            //Buscamos la id del usuario
            users? usuario = _context.users.AsNoTracking().FirstOrDefault(u => u.username == userName);

            if (usuario == null)
            {
                Console.WriteLine($"Usuario no encontrado -> ¿logueado?");
                return new BoolMensaje
                {
                    Exito = false,
                    Mensaje = "Usuario no encontrado -> ¿logueado?"
                };
            }

            //Creamos e insertamos la receta en la BBDD
            var nuevaReceta = new recetas
            {
                nombre = rec.nombre,
                descripcion = rec.descripcion,
                receta = rec.receta,
                fecha_creacion = DateTime.Now,
                id_usuario = usuario.id
            };
            _context.recetas.Add(nuevaReceta);
            _context.SaveChanges();

            //Recuperamos el ID de la receta
            recetas? idReceta = _context.recetas.AsNoTracking().Where(r => r.id_usuario == usuario.id).OrderByDescending(r => r.fecha_creacion).FirstOrDefault();

            return new BoolMensaje
            {
                Exito = true,
                Mensaje = idReceta.id.ToString()
            };
        }
        catch (Exception e)
        {
            Console.WriteLine($"Error guardando receta -> {e.Message}");
            return new BoolMensaje
            {
                Exito = false,
                Mensaje = $"Error guardando receta -> {e.Message}"
            };
        }
    }

}
*/