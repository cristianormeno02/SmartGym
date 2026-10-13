# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj files and restore dependencies
COPY ["src/SmartGym.WebApi/SmartGym.WebApi.csproj", "src/SmartGym.WebApi/"]
COPY ["src/SmartGym.Application/SmartGym.Application.csproj", "src/SmartGym.Application/"]
COPY ["src/SmartGym.Infrastructure/SmartGym.Infrastructure.csproj", "src/SmartGym.Infrastructure/"]
COPY ["src/SmartGym.Domain/SmartGym.Domain.csproj", "src/SmartGym.Domain/"]

RUN dotnet restore "src/SmartGym.WebApi/SmartGym.WebApi.csproj"

# Copy the rest of the source code and publish
COPY src/ src/
WORKDIR "/src/src/SmartGym.WebApi"
RUN dotnet publish "SmartGym.WebApi.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

COPY --from=build /app/publish .

EXPOSE 8080
ENV ASPNETCORE_ENVIRONMENT=Production

CMD ["sh", "-c", "ASPNETCORE_URLS=http://+:${PORT:-8080} dotnet SmartGym.WebApi.dll"]
