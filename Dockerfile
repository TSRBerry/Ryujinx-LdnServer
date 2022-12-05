FROM mcr.microsoft.com/dotnet/sdk:7.0-alpine AS build
LABEL "author"="Mary <contact@mary.zone>"
WORKDIR /source

COPY *.csproj .
RUN dotnet restore -r linux-musl-x64 /p:PublishReadyToRun=true

COPY . .
RUN dotnet publish -c release -o /app -r linux-musl-x64 --self-contained true --no-restore /p:ExtraDefineConstants=DISABLE_CLI /p:PublishTrimmed=true /p:PublishReadyToRun=true /p:PublishSingleFile=true

FROM mcr.microsoft.com/dotnet/runtime-deps:7.0-alpine-amd64
WORKDIR /app

# See: https://github.com/dotnet/announcements/issues/20
ENV \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    LC_ALL=en_US.UTF-8 \
    LANG=en_US.UTF-8

RUN apk add --no-cache icu-libs && addgroup -S appgroup && adduser -S appuser -G appgroup

COPY --from=build /app .
RUN chown -R appuser:appgroup /app
USER appuser

EXPOSE 30456
EXPOSE 8080
ENTRYPOINT ["./LanPlayServer"]
