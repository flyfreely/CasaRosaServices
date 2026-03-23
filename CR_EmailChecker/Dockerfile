# base runtime
FROM mcr.microsoft.com/dotnet/runtime:9.0 AS base
WORKDIR /app

# build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
# copy only the csproj first for restore layer caching
COPY CR_EmailChecker.csproj ./
RUN dotnet restore "CR_EmailChecker.csproj"
# now copy the rest
COPY . .
COPY ["./appsettings.docker.json", "appsettings.json"]
RUN dotnet build "CR_EmailChecker.csproj" -c $BUILD_CONFIGURATION -o /app/build

# publish
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "CR_EmailChecker.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# final image
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "CR_EmailChecker.dll"]
