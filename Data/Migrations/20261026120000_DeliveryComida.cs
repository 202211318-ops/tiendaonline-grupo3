using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TiendaOnline.Data.Migrations
{
    /// <summary>
    /// URPOnline Delivery (fase 2, FASE2_ESPECIFICACION.md §9.9.4): restaurantes, platos con embedding (pgvector),
    /// repartidores, pedidos de comida, ofertas, posiciones, conexiones del WebSocket, reembolsos y la política del
    /// soporte. La ubicación va en lat/lon (double precision): la base local no trae PostGIS; activarlo es el desafío A2.
    /// La tabla cambios_aplicados (idempotencia, B3) la crea el grupo.
    /// </summary>
    [DbContext(typeof(ShopDbContext))]
    [Migration("20261026120000_DeliveryComida")]
    public partial class DeliveryComida : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS vector;");
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS restaurantes (
                    id      serial PRIMARY KEY,
                    nombre  varchar(120) NOT NULL,
                    lat     double precision NOT NULL,
                    lon     double precision NOT NULL);

                CREATE TABLE IF NOT EXISTS platos (
                    id              serial PRIMARY KEY,
                    restaurante_id  integer NOT NULL REFERENCES restaurantes(id) ON DELETE CASCADE,
                    nombre          varchar(120) NOT NULL,
                    descripcion     varchar(500) NOT NULL DEFAULT '',
                    precio          numeric(10,2) NOT NULL CHECK (precio >= 0),
                    disponible      boolean NOT NULL DEFAULT true,
                    ingredientes    text[] NOT NULL DEFAULT '{}',
                    alergenos       text[] NOT NULL DEFAULT '{}',
                    embedding       vector(384));
                CREATE INDEX IF NOT EXISTS ix_platos_embedding_hnsw ON platos USING hnsw (embedding vector_cosine_ops);

                CREATE TABLE IF NOT EXISTS repartidores (
                    id              serial PRIMARY KEY,
                    cognito_sub     varchar(64) NOT NULL UNIQUE,
                    nombre          varchar(120) NOT NULL DEFAULT '',
                    disponible      boolean NOT NULL DEFAULT true,
                    lat             double precision,
                    lon             double precision,
                    actualizado_en  timestamptz NOT NULL DEFAULT now());

                CREATE TABLE IF NOT EXISTS pedidos_comida (
                    id              serial PRIMARY KEY,
                    cliente_sub     varchar(64) NOT NULL,
                    restaurante_id  integer NOT NULL REFERENCES restaurantes(id),
                    repartidor_id   integer REFERENCES repartidores(id),
                    estado          varchar(20) NOT NULL DEFAULT 'recibido'
                                    CHECK (estado IN ('recibido','ofrecido','aceptado','recogido','entregado','sin_repartidor','cancelado')),
                    version         integer NOT NULL DEFAULT 1,
                    total           numeric(10,2) NOT NULL CHECK (total >= 0),
                    lat             double precision NOT NULL,
                    lon             double precision NOT NULL,
                    creado_en       timestamptz NOT NULL DEFAULT now(),
                    actualizado_en  timestamptz NOT NULL DEFAULT now(),
                    borrado         boolean NOT NULL DEFAULT false);
                CREATE INDEX IF NOT EXISTS ix_pedidos_comida_cliente ON pedidos_comida (cliente_sub, actualizado_en);
                CREATE INDEX IF NOT EXISTS ix_pedidos_comida_repartidor ON pedidos_comida (repartidor_id, actualizado_en);

                CREATE TABLE IF NOT EXISTS pedido_items (
                    pedido_id        integer NOT NULL REFERENCES pedidos_comida(id) ON DELETE CASCADE,
                    plato_id         integer NOT NULL REFERENCES platos(id),
                    cantidad         integer NOT NULL CHECK (cantidad BETWEEN 1 AND 20),
                    precio_unitario  numeric(10,2) NOT NULL,
                    PRIMARY KEY (pedido_id, plato_id));

                CREATE TABLE IF NOT EXISTS ofertas (
                    pedido_id      integer NOT NULL REFERENCES pedidos_comida(id) ON DELETE CASCADE,
                    repartidor_id  integer NOT NULL REFERENCES repartidores(id),
                    intento        integer NOT NULL,
                    creada_en      timestamptz NOT NULL DEFAULT now(),
                    PRIMARY KEY (pedido_id, repartidor_id, intento));

                CREATE TABLE IF NOT EXISTS posiciones (
                    id                bigserial PRIMARY KEY,
                    repartidor_id     integer NOT NULL REFERENCES repartidores(id),
                    pedido_id         integer REFERENCES pedidos_comida(id),
                    secuencia         bigint NOT NULL,
                    lat               double precision NOT NULL,
                    lon               double precision NOT NULL,
                    hora_dispositivo  timestamptz NOT NULL,
                    hora_servidor     timestamptz NOT NULL DEFAULT now());
                CREATE INDEX IF NOT EXISTS ix_posiciones_pedido ON posiciones (pedido_id, hora_servidor);

                CREATE TABLE IF NOT EXISTS conexiones_ws (
                    connection_id  varchar(64) PRIMARY KEY,
                    pedido_id      integer,
                    cliente_sub    varchar(64) NOT NULL,
                    conectado_en   timestamptz NOT NULL DEFAULT now());
                CREATE INDEX IF NOT EXISTS ix_conexiones_ws_pedido ON conexiones_ws (pedido_id);

                CREATE TABLE IF NOT EXISTS reembolsos (
                    id            serial PRIMARY KEY,
                    pedido_id     integer NOT NULL REFERENCES pedidos_comida(id),
                    monto         numeric(10,2) NOT NULL CHECK (monto >= 0),
                    estado        varchar(20) NOT NULL DEFAULT 'pendiente',
                    aprobado_por  varchar(120),
                    creado_en     timestamptz NOT NULL DEFAULT now());

                CREATE TABLE IF NOT EXISTS politica_delivery (
                    id         serial PRIMARY KEY,
                    seccion    varchar(80) NOT NULL UNIQUE,
                    contenido  text NOT NULL,
                    embedding  vector(384));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TABLE IF EXISTS politica_delivery, reembolsos, conexiones_ws, posiciones, ofertas,
                    pedido_items, pedidos_comida, repartidores, platos, restaurantes;
                """);
        }
    }
}
