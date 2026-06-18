FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY HaulScout.slnx ./
COPY src/HaulScout.Domain/HaulScout.Domain.csproj src/HaulScout.Domain/
COPY src/HaulScout.Application/HaulScout.Application.csproj src/HaulScout.Application/
COPY src/HaulScout.Infrastructure/HaulScout.Infrastructure.csproj src/HaulScout.Infrastructure/
COPY src/HaulScout.Api/HaulScout.Api.csproj src/HaulScout.Api/
RUN dotnet restore src/HaulScout.Api/HaulScout.Api.csproj

COPY . .
RUN dotnet publish src/HaulScout.Api/HaulScout.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
EXPOSE 8080
ARG SOURCE_REPOSITORY_URL=""
ARG IMAGE_DESCRIPTION="HaulScout ASP.NET Core API"
LABEL org.opencontainers.image.source=$SOURCE_REPOSITORY_URL
LABEL org.opencontainers.image.description=$IMAGE_DESCRIPTION
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "HaulScout.Api.dll"]
