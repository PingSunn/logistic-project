# Build the license server + admin CLI into a single small runtime image.
# Multi-arch: build with `docker buildx build --platform linux/arm64,linux/amd64 ...`
# or just `docker build .` on the Pi5 (arm64) for a native image.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY CargoFit.LicenseServer.Core/ ./CargoFit.LicenseServer.Core/
COPY CargoFit.LicenseServer/      ./CargoFit.LicenseServer/
COPY CargoFit.LicenseAdmin/       ./CargoFit.LicenseAdmin/

RUN dotnet publish CargoFit.LicenseServer/CargoFit.LicenseServer.csproj \
        -c Release -o /out/server
RUN dotnet publish CargoFit.LicenseAdmin/CargoFit.LicenseAdmin.csproj \
        -c Release -o /out/admin


FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /out/server /app/server
COPY --from=build /out/admin  /app/admin

# Convenience: `license-admin` resolves on PATH inside the container.
RUN ln -s /app/admin/license-admin /usr/local/bin/license-admin

ENV LICENSE_SERVER_DB_PATH=/data/licenses.db
ENV ASPNETCORE_URLS=http://0.0.0.0:5080
EXPOSE 5080
VOLUME /data

ENTRYPOINT ["dotnet", "/app/server/CargoFit.LicenseServer.dll"]
