# Build Image
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

# Copy csproj files and restore as distinct layers
COPY Directory.Build.props .
COPY Lis.Core/*.csproj Lis.Core/
COPY Lis.Persistence/*.csproj Lis.Persistence/
COPY Lis.Tools/*.csproj Lis.Tools/
COPY Lis.Agent/*.csproj Lis.Agent/
COPY Lis.Channels/*.csproj Lis.Channels/
COPY Lis.Providers/*.csproj Lis.Providers/
COPY Lis.Api/*.csproj Lis.Api/
RUN dotnet restore "Lis.Api/Lis.Api.csproj"

# Copy .editorconfig for analyzer rules
COPY .editorconfig .

# Copy and publish app
COPY Lis.Core/ Lis.Core/
COPY Lis.Persistence/ Lis.Persistence/
COPY Lis.Tools/ Lis.Tools/
COPY Lis.Agent/ Lis.Agent/
COPY Lis.Channels/ Lis.Channels/
COPY Lis.Providers/ Lis.Providers/
COPY Lis.Api/ Lis.Api/
RUN touch Lis.Api/.env
RUN dotnet publish "Lis.Api/Lis.Api.csproj" -c Release --no-restore -o /app

# Runtime Image
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
EXPOSE 3010
WORKDIR /app

# Add globalization and timezones
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    LC_ALL=pt_BR.UTF-8 \
    LANG=pt_BR.UTF-8
RUN apk add --no-cache icu-data-full icu-libs

USER app
COPY --from=build /app .
ENTRYPOINT ["./Lis.Api"]
