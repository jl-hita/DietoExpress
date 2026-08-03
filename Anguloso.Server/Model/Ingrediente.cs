using Anguloso.Server.Models;

namespace Anguloso.Server.Model;
/*
CREATE TABLE ingredientes (
    id SERIAL PRIMARY KEY,
    nombre VARCHAR(100) NOT NULL,
    comprado BOOLEAN NOT NULL DEFAULT FALSE,
    descripcion VARCHAR(100),
    cantidad INTEGER NOT NULL DEFAULT 1,
    unidad VARCHAR(20) NOT NULL,
    id_usuario INTEGER --Referencia a un usuario por si está en su lista de la compra
);
 */
/*
public class Ingrediente
{
    public string Nombre { get; set; } = ""; //Nombre del producto
    public bool Comprado { get; set; } //Tachado de la lista
    public string? Descripcion { get; set; } //Opcional: descripción más extensa del producto
    public int Cantidad { get; set; } //Cantidad en peso o unidades
    public string Unidad { get; set; } = "gramos"; //Magnitud (unidades, gramos, etc)

    public Ingrediente() { }

    public Ingrediente(string nombre, bool comprado, string? descripcion, int cantidad, string unidad)
    {
        Nombre = nombre;
        Comprado = comprado;
        Descripcion = descripcion;
        Cantidad = cantidad;
        Unidad = unidad;
    }

    //Constructor con el objeto de la bbdd
    public Ingrediente(ingredientes ingrediente)
    {
        Nombre = ingrediente.nombre;
        Comprado = ingrediente.comprado;
        Descripcion = ingrediente.descripcion;
        Cantidad = ingrediente.cantidad;
        Unidad = ingrediente.unidad;
    }

}
public class ListaCompra
{
    public List<Ingrediente> Ingredientes { get; set; } = new();

    public ListaCompra() { }

    //Crea una lista de objetos Ingredientes con objetos de la bbdd
    public ListaCompra(List<ingredientes> ings)
    {
        foreach (ingredientes ing in ings)
        {
            Ingrediente ingre = new Ingrediente(ing);
            Ingredientes.Add(ingre);
        }
    }
}

public class UsuarioIngrediente
{
    public Usuario Usuario { get; set; } = new();
    public Ingrediente Ingrediente {  get; set; } = new();
}
*/