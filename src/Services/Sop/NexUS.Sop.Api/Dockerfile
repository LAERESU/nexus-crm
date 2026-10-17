# Build context: root repo. Contoh: docker build -f src/Services/Sop/NexUS.Sop.Api/Dockerfile -t nexus/sop .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["global.json", "Directory.Build.props", "Directory.Packages.props", "./"]
COPY ["src/BuildingBlocks/", "src/BuildingBlocks/"]
COPY ["src/Services/Sop/NexUS.Sop.Domain/NexUS.Sop.Domain.csproj", "src/Services/Sop/NexUS.Sop.Domain/"]
COPY ["src/Services/Sop/NexUS.Sop.Application/NexUS.Sop.Application.csproj", "src/Services/Sop/NexUS.Sop.Application/"]
COPY ["src/Services/Sop/NexUS.Sop.Infrastructure/NexUS.Sop.Infrastructure.csproj", "src/Services/Sop/NexUS.Sop.Infrastructure/"]
COPY ["src/Services/Sop/NexUS.Sop.Api/NexUS.Sop.Api.csproj", "src/Services/Sop/NexUS.Sop.Api/"]
RUN dotnet restore "src/Services/Sop/NexUS.Sop.Api/NexUS.Sop.Api.csproj"
COPY ["src/Services/Sop/NexUS.Sop.Domain/", "src/Services/Sop/NexUS.Sop.Domain/"]
COPY ["src/Services/Sop/NexUS.Sop.Application/", "src/Services/Sop/NexUS.Sop.Application/"]
COPY ["src/Services/Sop/NexUS.Sop.Infrastructure/", "src/Services/Sop/NexUS.Sop.Infrastructure/"]
COPY ["src/Services/Sop/NexUS.Sop.Api/", "src/Services/Sop/NexUS.Sop.Api/"]
RUN dotnet publish "src/Services/Sop/NexUS.Sop.Api/NexUS.Sop.Api.csproj" \
    -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true
COPY --from=build /app/publish .
# user non-root bawaan image .NET
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "NexUS.Sop.Api.dll"]
