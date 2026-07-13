# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, isolated from the rest of the source, so this layer only invalidates when a
# csproj/package reference actually changes — not on every code edit.
COPY Shush/Shush.csproj Shush/
COPY Shush.Design/Shush.Design.csproj Shush.Design/
RUN dotnet restore Shush.Design/Shush.Design.csproj

COPY Shush/ Shush/
COPY Shush.Design/ Shush.Design/
RUN dotnet publish Shush.Design/Shush.Design.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .
# Recipe steps (TemplatedCopyFilesStep/CopyFilesStep) resolve this relative to the process's
# working directory at runtime — a genuine runtime dependency, not just project source.
COPY FilesToTransfer/ ./FilesToTransfer/

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Shush.Design.dll"]
