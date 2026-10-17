# Build context: root repo. Contoh: docker build -f src/ApiGateway/NexUS.Gateway.Api/Dockerfile -t nexus/gateway .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["global.json", "Directory.Build.props", "Directory.Packages.props", "./"]
COPY ["src/BuildingBlocks/", "src/BuildingBlocks/"]
COPY ["src/ApiGateway/NexUS.Gateway.Api/NexUS.Gateway.Api.csproj", "src/ApiGateway/NexUS.Gateway.Api/"]
RUN dotnet restore "src/ApiGateway/NexUS.Gateway.Api/NexUS.Gateway.Api.csproj"
COPY ["src/ApiGateway/NexUS.Gateway.Api/", "src/ApiGateway/NexUS.Gateway.Api/"]
RUN dotnet publish "src/ApiGateway/NexUS.Gateway.Api/NexUS.Gateway.Api.csproj" \
    -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true
COPY --from=build /app/publish .
# user non-root bawaan image .NET
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "NexUS.Gateway.Api.dll"]
