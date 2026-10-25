# Web-Version des Tupfer Designers als statische Seite mit nginx (z. B. für CapRover).
# Bauen aus dem Hauptverzeichnis: docker build -f deploy/Dockerfile -t tupfer-web .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
# Emscripten (wasm-tools) braucht Python
RUN apt-get update && apt-get install -y --no-install-recommends python3 && rm -rf /var/lib/apt/lists/*
RUN dotnet workload install wasm-tools
WORKDIR /src
# Erst nur die Projektdateien, damit die Pakete im Docker-Cache bleiben
COPY Directory.Build.props ./
COPY Tupfer.Designer/Tupfer.Designer.csproj Tupfer.Designer/
COPY Tupfer.Designer.Browser/Tupfer.Designer.Browser.csproj Tupfer.Designer.Browser/
RUN dotnet restore Tupfer.Designer.Browser
COPY Tupfer.Designer/ Tupfer.Designer/
COPY Tupfer.Designer.Browser/ Tupfer.Designer.Browser/
COPY examples/ examples/
RUN dotnet publish Tupfer.Designer.Browser -c Release -o /publish --no-restore

FROM nginx:1.27-alpine
COPY deploy/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /publish/wwwroot /usr/share/nginx/html
EXPOSE 80
