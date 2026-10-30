# Build backend Dearly (.NET 10) cho Render/VPS
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY DrinkWater.slnx ./
COPY src/DrinkWater.Api/DrinkWater.Api.csproj src/DrinkWater.Api/
RUN dotnet restore src/DrinkWater.Api/DrinkWater.Api.csproj
COPY . .
RUN dotnet publish src/DrinkWater.Api/DrinkWater.Api.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "DrinkWater.Api.dll"]
