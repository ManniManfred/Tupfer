# Web-Version des TipToy Designers als statische Seite mit nginx (z. B. für CapRover).
# Bauen aus dem Hauptverzeichnis: docker build -f deploy/Dockerfile -t tiptoy-web .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
# Emscripten (wasm-tools) braucht Python
RUN apt-get update && apt-get install -y --no-install-recommends python3 && rm -rf /var/lib/apt/lists/*
RUN dotnet workload install wasm-tools
WORKDIR /src
# Erst nur die Projektdateien, damit die Pakete im Docker-Cache bleiben
COPY Directory.Build.props ./
COPY TipToy.Designer/TipToy.Designer.csproj TipToy.Designer/
COPY TipToy.Designer.Browser/TipToy.Designer.Browser.csproj TipToy.Designer.Browser/
RUN dotnet restore TipToy.Designer.Browser
COPY TipToy.Designer/ TipToy.Designer/
COPY TipToy.Designer.Browser/ TipToy.Designer.Browser/
COPY examples/ examples/
RUN dotnet publish TipToy.Designer.Browser -c Release -o /publish --no-restore

FROM nginx:1.27-alpine
COPY deploy/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /publish/wwwroot /usr/share/nginx/html
EXPOSE 80
