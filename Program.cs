using Microsoft.EntityFrameworkCore;using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.DataProtection;
using Amazon.S3;
using Amazon.SecretsManager;
using Amazon.CloudWatch;
using Amazon.EventBridge;
using TiendaOnline.Data;
using TiendaOnline.Services;
var builder=WebApplication.CreateBuilder(args);
builder.Services.AddControllersWithViews();
builder.Services.AddHttpContextAccessor();
var db=builder.Configuration.GetConnectionString("Postgres")?? throw new InvalidOperationException("Configure ConnectionStrings:Postgres; apunte a PgBouncer 6432 en AWS.");
// En AWS la contraseña de RDS se lee de Secrets Manager (AWS SDK); en tu PC se usa appsettings.json.
db=DatabaseSecret.BuildConnectionString(builder.Configuration,db);
builder.Services.AddDbContext<ShopDbContext>(o=>o.UseNpgsql(db));
// Fase 3 (B3): el catalogo lee de la replica de RDS (PostgresLectura); sin replica, del primario.
builder.Services.AddDbContext<ShopReadDbContext>(o=>o.UseNpgsql(DatabaseSecret.BuildReadConnectionString(builder.Configuration,db))
 .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));
var redis=builder.Configuration.GetConnectionString("Redis");
if(!string.IsNullOrWhiteSpace(redis)) builder.Services.AddStackExchangeRedisCache(o=>o.Configuration=redis);
else if(builder.Environment.IsDevelopment()) builder.Services.AddDistributedMemoryCache();
else throw new InvalidOperationException("Redis es obligatorio fuera de Development para compartir sesiones entre EC2.");
// Claves que cifran la cookie de sesión: en Redis (ElastiCache) para que todas las EC2 detrás del ALB las compartan.
var dataProtection=builder.Services.AddDataProtection().SetApplicationName("URPOnline");
if(!string.IsNullOrWhiteSpace(redis))
 dataProtection.PersistKeysToStackExchangeRedis(StackExchange.Redis.ConnectionMultiplexer.Connect(redis),"URPOnline-DataProtection-Keys");
builder.Services.AddSession(o=>{
 o.Cookie.Name=".URPOnline.Session";o.Cookie.HttpOnly=true;
 // SameAsRequest: la cookie es Secure cuando el visitante entra por HTTPS (ALB con ACM o CloudFront); en el laboratorio el ALB usa HTTP.
 o.Cookie.SecurePolicy=CookieSecurePolicy.SameAsRequest;o.Cookie.SameSite=SameSiteMode.Lax;
 o.Cookie.IsEssential=true;o.IdleTimeout=TimeSpan.FromMinutes(30);
});
builder.Services.AddScoped<CartService>();builder.Services.AddScoped<BusinessEvents>();
builder.Services.AddScoped<BusinessMetrics>();
// Eventos de negocio hacia Amazon EventBridge (los consumen los agentes por SQS).
builder.Services.AddScoped<StoreEventPublisher>();
// Búsqueda semántica: modelo local ONNX + pgvector en PostgreSQL.
builder.Services.AddSingleton<EmbeddingService>();
builder.Services.AddScoped<ProductVectorStore>();
builder.Services.AddHostedService<ProductEmbeddingIndexer>();
builder.Services.AddDefaultAWSOptions(builder.Configuration.GetAWSOptions());
builder.Services.AddAWSService<IAmazonS3>();
builder.Services.AddAWSService<IAmazonSecretsManager>();
builder.Services.AddAWSService<IAmazonCloudWatch>();
builder.Services.AddAWSService<IAmazonEventBridge>();
// Motor de IA: catalogo de modelos abiertos/gratuitos, keys en Secrets Manager, cliente comun y "Probar conexion".
builder.Services.AddHttpClient("llm", c => c.Timeout = TimeSpan.FromSeconds(60));
builder.Services.AddSingleton<TiendaOnline.Services.Llm.LlmCatalog>();
builder.Services.AddSingleton<TiendaOnline.Services.Llm.LlmSettingsStore>();
builder.Services.AddSingleton<TiendaOnline.Services.Llm.LlmProviderFactory>();
builder.Services.AddSingleton<TiendaOnline.Services.Llm.LlmConnectionTester>();
// Agentes de IA: guardrails (3 capas), RAG estructural de los SLA y agente de soporte.
builder.Services.AddSingleton<TiendaOnline.Services.Agents.InputGuard>();
builder.Services.AddSingleton<TiendaOnline.Services.Agents.OutputGuard>();
builder.Services.AddScoped<TiendaOnline.Services.Agents.SlaSectionStore>();
builder.Services.AddScoped<TiendaOnline.Services.Agents.SupportAgent>();
// Agentes de stock (ReAct), carrito (generador/critico) y analista (hipotesis/verificacion) + sus herramientas.
builder.Services.AddScoped<TiendaOnline.Services.Agents.IAgentTools, TiendaOnline.Services.Agents.SqlAgentTools>();
builder.Services.AddScoped<TiendaOnline.Services.Agents.StockAgent>();
builder.Services.AddScoped<TiendaOnline.Services.Agents.CartAgent>();
builder.Services.AddScoped<TiendaOnline.Services.Agents.AnalystAgent>();
builder.Services.AddScoped<TiendaOnline.Services.Agents.IdempotencyStore>();
builder.Services.AddScoped<TiendaOnline.Services.Agents.SlaSeeder>();
// Fase 2, URPOnline Delivery: carta, búsqueda de platos, soporte del delivery y sus datos de prueba.
builder.Services.AddScoped<TiendaOnline.Services.Comida.ComidaStore>();
builder.Services.AddScoped<TiendaOnline.Services.Comida.SoporteDelivery>();
builder.Services.AddScoped<TiendaOnline.Services.Comida.ComidaSeeder>();
// Fase 3, Bloque G: factura de cada pedido en el sistema contable (JSON-RPC) y aviso al navegador por SignalR (G10).
// Con Redis, el backplane hace que el aviso llegue aunque el navegador esté conectado a otro pod.
var signalR=builder.Services.AddSignalR();
if(!string.IsNullOrWhiteSpace(redis)) signalR.AddStackExchangeRedis(redis);
builder.Services.AddSingleton<TiendaOnline.Services.Comprobantes.AvisoDeSunat>();
builder.Services.AddSingleton<TiendaOnline.Services.Comprobantes.Facturador>();
var app=builder.Build();
if(!app.Environment.IsDevelopment()){app.UseExceptionHandler("/Home/Error");app.UseHsts();}
app.UseForwardedHeaders(new ForwardedHeadersOptions {ForwardedHeaders=ForwardedHeaders.XForwardedFor|ForwardedHeaders.XForwardedProto,KnownIPNetworks={ },KnownProxies={ }});
app.UseStaticFiles();app.UseRouting();app.UseSession();
app.MapGet("/health",()=>Results.Ok(new{status="ok"}));
app.MapHub<TiendaOnline.Services.Comprobantes.ComprobantesHub>(TiendaOnline.Services.Comprobantes.ComprobantesHub.Ruta);
app.MapControllerRoute("default","{controller=Home}/{action=Index}/{id?}");
if(app.Configuration.GetValue<bool>("InitializeDatabase")){
 using var scope=app.Services.CreateScope();var database=scope.ServiceProvider.GetRequiredService<ShopDbContext>();
 // Migraciones EF Core versionadas (Data/Migrations): tablas + pgvector + índice HNSW. Luego datos de ejemplo.
 // pg_advisory_lock: si varias EC2 arrancan a la vez, solo una migra; las demás esperan y encuentran todo aplicado.
 await database.Database.OpenConnectionAsync();
 try {
  await database.Database.ExecuteSqlRawAsync("SELECT pg_advisory_lock(727001)");
  await database.Database.MigrateAsync();await SeedData.LoadAsync(database);
  // Fase 2: tabla de idempotencia y SLA de ejemplo para el RAG del agente de soporte.
  await scope.ServiceProvider.GetRequiredService<TiendaOnline.Services.Agents.IdempotencyStore>().EnsureSchemaAsync();
  await TiendaOnline.Services.Agents.CartRecovery.CrearTablaAsync(database);   // control de 1 correo cada 24 h
  await scope.ServiceProvider.GetRequiredService<TiendaOnline.Services.Agents.SlaSeeder>().SeedAsync();
  await scope.ServiceProvider.GetRequiredService<TiendaOnline.Services.Comida.ComidaSeeder>().SeedAsync();   // delivery
 } finally {
  await database.Database.ExecuteSqlRawAsync("SELECT pg_advisory_unlock(727001)");
  await database.Database.CloseConnectionAsync();
 }
}
app.Run();
