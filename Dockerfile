# Fase 3 - Imagen de la tienda en contenedor (multietapa).
# Etapa 1: compila con el SDK. Etapa 2: ejecuta solo con el runtime (imagen mas pequena y segura).
# El modelo de embeddings (ml-models/) se descarga con scripts/descargar-modelo.ps1 ANTES de construir,
# o se monta/baja en el arranque; no se versiona en git por su tamano.

# ---------- build ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY TiendaOnline.csproj .
# Fase 3, Bloque G: la tienda usa el contrato y el canal JSON-RPC del sistema contable.
COPY contable/URPOnline.Contable.csproj contable/packages.lock.json contable/
RUN dotnet restore TiendaOnline.csproj
COPY . .
RUN dotnet publish TiendaOnline.csproj -c Release -o /app --no-restore -p:ErrorOnDuplicatePublishOutputFiles=false

# ---------- runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
# Usuario no root (buena practica de seguridad): las imagenes .NET 8 ya traen el usuario 'app' (UID $APP_UID).
COPY --from=build --chown=$APP_UID /app .
USER $APP_UID
# Kestrel escucha en 8080 dentro del contenedor (detras del ALB/Ingress).
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
# Health check del contenedor (coincide con /health del ALB/Ingress). La imagen aspnet no trae curl ni wget,
# asi que se pide /health con bash (/dev/tcp) y se exige un HTTP 200.
HEALTHCHECK --interval=15s --timeout=3s --retries=3 CMD bash -c 'exec 3<>/dev/tcp/127.0.0.1/8080 && printf "GET /health HTTP/1.0\r\nHost: localhost\r\n\r\n" >&3 && head -1 <&3 | grep -q " 200 "'
ENTRYPOINT ["dotnet", "TiendaOnline.dll"]
