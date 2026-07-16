# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# A separate "restore csproj-only, then copy source, then publish --no-restore" layer-caching
# step corrupts the static web assets manifest here: the framework's own blazor.web.js entries
# go missing (reproduced and confirmed outside Docker), causing 404s on that script at runtime.
# Restoring and publishing in one shot with full source present avoids it.
COPY Shush/ Shush/
COPY Shush.Design/ Shush.Design/
RUN dotnet publish Shush.Design/Shush.Design.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Shush.Design.dll"]
