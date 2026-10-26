using System.Text.Json;
using Amazon.Extensions.NETCore.Setup;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Npgsql;
namespace TiendaOnline.Services;

// Lee la contraseña de RDS desde AWS Secrets Manager con el AWS SDK (GetSecretValue).
// En la EC2 la cadena de conexión NO trae contraseña: llega Database:SecretArn y el rol IAM
// de la instancia tiene permiso secretsmanager:GetSecretValue solo sobre ese secreto.
// En tu PC (sin Database:SecretArn) se usa la cadena de appsettings.json tal cual.
public static class DatabaseSecret {
 public static string BuildConnectionString(IConfiguration config, string baseConnectionString) {
  var secretArn = config["Database:SecretArn"];
  if (string.IsNullOrWhiteSpace(secretArn)) return baseConnectionString;

  using var client = config.GetAWSOptions().CreateServiceClient<IAmazonSecretsManager>();
  var secret = client.GetSecretValueAsync(new GetSecretValueRequest { SecretId = secretArn }).GetAwaiter().GetResult();
  using var json = JsonDocument.Parse(secret.SecretString);
  var builder = new NpgsqlConnectionStringBuilder(baseConnectionString) {
   Password = json.RootElement.GetProperty("password").GetString()
  };
  if (json.RootElement.TryGetProperty("username", out var user)) builder.Username = user.GetString();
  return builder.ConnectionString;
 }
 // Fase 3 (B3): conexion de LECTURA. Si hay ConnectionStrings:PostgresLectura (endpoint de la replica de RDS),
 // se usa con la misma contrasena de Secrets Manager (la replica comparte usuarios con el primario).
 // Si no hay replica, se lee del primario: la tienda funciona igual con o sin ella.
 public static string BuildReadConnectionString(IConfiguration config, string primaryConnectionString) {
  var lectura = config.GetConnectionString("PostgresLectura");
  return string.IsNullOrWhiteSpace(lectura) ? primaryConnectionString : BuildConnectionString(config, lectura);
 }
}
