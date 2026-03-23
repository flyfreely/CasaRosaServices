FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY CR_Admin.csproj .
RUN dotnet restore
COPY . .
RUN cp appsettings.docker.json appsettings.json
RUN dotnet publish -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "CR_Admin.dll"]
