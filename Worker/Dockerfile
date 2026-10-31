# Fase 3 (F1) - Imagen del importador de proveedores: corre como CronJob en EKS (k8s/proveedores-cronjob.yaml).
# Se construye desde la RAIZ del repositorio (usa nuget.config y Directory.Build.props):
#   docker build -f Worker/Dockerfile -t <ECR_WORKER_IMAGE> .

# ---------- build ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY nuget.config Directory.Build.props ./
COPY Worker/URPOnline.SupplierWorker.csproj Worker/packages.lock.json Worker/
RUN dotnet restore Worker/URPOnline.SupplierWorker.csproj --locked-mode
COPY Worker/ Worker/
RUN dotnet publish Worker/URPOnline.SupplierWorker.csproj -c Release -o /app --no-restore

# ---------- runtime ----------
# Es un programa de consola: basta el runtime de .NET, sin ASP.NET.
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
COPY --from=build --chown=$APP_UID /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "URPOnline.SupplierWorker.dll"]
