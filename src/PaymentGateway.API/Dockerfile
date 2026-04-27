FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project files first so Docker layer cache is reused on code-only changes
COPY ["src/PaymentGateway.API/PaymentGateway.API.csproj", "src/PaymentGateway.API/"]
COPY ["src/PaymentGateway.Core/PaymentGateway.Core.csproj", "src/PaymentGateway.Core/"]
COPY ["src/PaymentGateway.Infrastructure/PaymentGateway.Infrastructure.csproj", "src/PaymentGateway.Infrastructure/"]
RUN dotnet restore "src/PaymentGateway.API/PaymentGateway.API.csproj"

COPY . .
WORKDIR "/src/src/PaymentGateway.API"
RUN dotnet build "PaymentGateway.API.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "PaymentGateway.API.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "PaymentGateway.API.dll"]
