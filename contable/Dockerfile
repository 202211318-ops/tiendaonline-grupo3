# Fase 3 (Bloque G) - Imagen del sistema contable (Facade + Gateway a SUNAT). Va en SU PROPIA subred,
# detras de firewall con NAT (desafio G4). Se construye desde la RAIZ del repositorio:
#   docker build -f contable/Dockerfile -t <ECR_CONTABLE_IMAGE> .

# ---------- build ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY nuget.config Directory.Build.props ./
COPY contable/URPOnline.Contable.csproj contable/packages.lock.json contable/
RUN dotnet restore contable/URPOnline.Contable.csproj --locked-mode
COPY contable/ contable/
RUN dotnet publish contable/URPOnline.Contable.csproj -c Release -o /app --no-restore

# ---------- runtime ----------
# Programa de consola: basta el runtime de .NET. ML-KEM y ML-DSA (G3) necesitan OpenSSL 3.5 o superior en Linux.
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
COPY --from=build --chown=$APP_UID /app .
USER $APP_UID
EXPOSE 7443
ENTRYPOINT ["dotnet", "URPOnline.Contable.dll", "servidor"]
