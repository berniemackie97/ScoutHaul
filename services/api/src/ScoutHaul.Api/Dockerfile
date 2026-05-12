FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ScoutHaul.slnx ./
COPY src/ScoutHaul.Domain/ScoutHaul.Domain.csproj src/ScoutHaul.Domain/
COPY src/ScoutHaul.Application/ScoutHaul.Application.csproj src/ScoutHaul.Application/
COPY src/ScoutHaul.Infrastructure/ScoutHaul.Infrastructure.csproj src/ScoutHaul.Infrastructure/
COPY src/ScoutHaul.Api/ScoutHaul.Api.csproj src/ScoutHaul.Api/
RUN dotnet restore src/ScoutHaul.Api/ScoutHaul.Api.csproj

COPY . .
RUN dotnet publish src/ScoutHaul.Api/ScoutHaul.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
EXPOSE 8080
ARG SOURCE_REPOSITORY_URL=""
ARG IMAGE_DESCRIPTION="ScoutHaul ASP.NET Core API"
LABEL org.opencontainers.image.source=$SOURCE_REPOSITORY_URL
LABEL org.opencontainers.image.description=$IMAGE_DESCRIPTION
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "ScoutHaul.Api.dll"]
