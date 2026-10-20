# syntax=docker/dockerfile:1
#
# The Promuse API as an image.
#
#   docker build -f deploy/docker/api.Dockerfile -t promuse-api .
#
# 上下文是仓库根目录 / The build context is the REPOSITORY ROOT, not server/: the server compiles the
# wire contracts and the scoring rules straight out of Assets/ and loads the charts from there, so
# those folders have to be in the context. api.Dockerfile.dockerignore, beside this file, keeps the
# rest of the Unity project out of it.
#
# Running it:
#   docker run ... promuse-api --migrate   apply pending migrations and exit (a deploy's first step)
#   docker run ... promuse-api             serve on port 8080
#
# Configuration comes from the environment, as anywhere outside a laptop:
#   ConnectionStrings__Promuse              required by both
#   Jwt__SigningKey                         required to serve, 32+ characters
#   Notices__BannerDirectory                where uploaded notice pictures go; mount a volume there
#   ASPNETCORE_FORWARDEDHEADERS_ENABLED     true behind the reverse proxy, so limits see real callers

# ------------------------------------------------------------------------------------- build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# 先只拷项目文件 / Restore from the project files alone, so a code change reuses the cached
# package layer and only a dependency change downloads anything.
COPY server/Directory.Build.props server/
COPY server/src/Promuse.Api/Promuse.Api.csproj server/src/Promuse.Api/
COPY server/src/Promuse.Contracts/Promuse.Contracts.csproj server/src/Promuse.Contracts/
COPY server/src/Promuse.Persistence/Promuse.Persistence.csproj server/src/Promuse.Persistence/
COPY server/src/Promuse.Scoring/Promuse.Scoring.csproj server/src/Promuse.Scoring/
RUN dotnet restore server/src/Promuse.Api/Promuse.Api.csproj

COPY server/ server/
COPY Assets/Promuse.Net/Contracts/ Assets/Promuse.Net/Contracts/
COPY Assets/Source/Script/Core/ Assets/Source/Script/Core/
COPY Assets/Beatmaps/ Assets/Beatmaps/

RUN dotnet publish server/src/Promuse.Api/Promuse.Api.csproj \
        --configuration Release --no-restore --output /app \
        -p:UseAppHost=false

# ------------------------------------------------------------------------------------- runtime

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# JSON logs, one object a line: the log collector reads fields (level, category, trace id) rather
# than parsing prose. 8080 is the image's own default, written out because the proxy relies on it.
ENV ASPNETCORE_HTTP_PORTS=8080 \
    Logging__Console__FormatterName=json

# Npgsql offers Postgres GSS encryption first and loads the Kerberos library to do it. The base
# image has none, so every connection printed "Cannot load library libgssapi_krb5.so.2" - harmless,
# the connection carries on, but one raw line in the JSON log for every connection opened.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app ./

# 非 root / The image's own unprivileged user ($APP_UID). It owns only the one folder the API
# writes - uploaded notice pictures, App_Data/banners by default - and nothing it runs.
RUN mkdir -p /app/App_Data/banners && chown -R "$APP_UID" /app/App_Data
USER $APP_UID

EXPOSE 8080
ENTRYPOINT ["dotnet", "Promuse.Api.dll"]
