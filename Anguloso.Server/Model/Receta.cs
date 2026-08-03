namespace Anguloso.Server.Model;

/*
CREATE TABLE recetas (
    id SERIAL PRIMARY KEY,
    nombre VARCHAR(20) NOT NULL,
    descripcion TEXT,
    receta TEXT,
    fecha_creacion TIMESTAMP,
    id_usuario INTEGER NOT NULL,
    CONSTRAINT fk_usuario FOREIGN KEY(id_usuario) REFERENCES users(id)
);
*/

public class Receta
{
}
