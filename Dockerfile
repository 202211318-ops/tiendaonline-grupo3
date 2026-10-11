# Fase 3 - Imagen de la tienda en contenedor (multietapa).
# Etapa 1: compila con el SDK. Etapa 2: ejecuta solo con el runtime (imagen mas pequena y segura).
# El modelo de embeddings (ml-models/) se descarga con scripts/descargar-modelo.ps1 ANTES de construir,
# o se monta/baja en el arranque; no se versiona en git por su tamano.

# ---------- build ----------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY TiendaOnline.csproj .
RUN dotnet restore TiendaOnline.csproj
COPY . .
RUN dotnet publish TiendaOnline.csproj -c Release -o /app --no-restore

# ---------- runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
# Usuario no root (buena practica de seguridad).
RUN adduser --disabled-password --gecos "" appuser && chown -R appuser /app
USER appuser
COPY --from=build /app .
# Kestrel escucha en 8080 dentro del contenedor (detras del ALB/Ingress).
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
# Health check del contenedor (coincide con /health del ALB/Ingress).
HEALTHCHECK --interval=15s --timeout=3s --retries=3 CMD wget -qO- http://localhost:8080/health || exit 1
ENTRYPOINT ["dotnet", "TiendaOnline.dll"]
