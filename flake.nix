{
  description = "Ryujinx-LdnServer flake env";

  inputs = {
    flake-utils.url = "github:numtide/flake-utils";
    nixpkgs.url = "github:NixOS/nixpkgs/nixos-23.11";

    ryujinx-ldn-website = { url = "github:Ryujinx/Ryujinx-Ldn-Website"; };
    ryujinx-ldn-website.inputs.nixpkgs.follows = "nixpkgs";
    ryujinx-ldn-website.inputs.flake-utils.follows = "flake-utils";
  };

  outputs = { self, nixpkgs, flake-utils, ryujinx-ldn-website }:
    let
      ldn_overlay = final: prev: {
        ryujinx-ldn = with final;
          buildDotnetModule rec {
            pname = "ryujinx-ldn-server";
            version = "1.0.0";

            src = self;

            projectFile = "LanPlayServer.sln";
            nugetDeps = ./deps.nix;

            dotnet-sdk = dotnetCorePackages.sdk_7_0;
            dotnet-runtime = dotnetCorePackages.runtime_7_0;
            selfContainedBuild = false;

            dotnetFlags = [ "-p:PublishAOT=false" ];

            executables = [ "LanPlayServer" ];
          };

        redis-json = with final;
          rustPlatform.buildRustPackage rec {
            pname = "RedisJSON";
            version = "2.4.7";

            src = fetchgit {
              url = "https://github.com/RedisJSON/${pname}";
              fetchSubmodules = true;
              rev = "v${version}";
              hash = "sha256-uDDDnTlTLe+O1H3B9uVI+l/b34ZmfTg1E7V8whmkn4g=";
            };

            cargoHash = "sha256-LR2qlg19+ltdBZyO65EAYc7YL8Vo5HTa8YwNkWI8OeU=";

            cargoPatches = [ ./patches/RedisJSON-config-fix.patch ];

            nativeBuildInputs = [ rustPlatform.bindgenHook ];

            cargoTestFlags = [ "--features=test" ];

            meta = with lib; {
              description = "A JSON data type for Redis";
              homepage = "https://github.com/RedisJSON/${pname}";
              license = [
                licenses.sspl
                {
                  shortName = "RSALv2";
                  fullName = "Redis Source Available License 2.0";
                  url = "https://redis.com/legal/rsalv2-agreement/";
                  free = false;
                  redistributable = true;
                }
              ];
            };
          };

      };
    in flake-utils.lib.eachDefaultSystem (system:
      let
        pkgs = import nixpkgs {
          inherit system;
          overlays = [
            self.overlays."${system}"
            ryujinx-ldn-website.overlays."${system}"
          ];
        };
      in {
        packages = {
          default = self.packages.${system}.ryujinx-ldn;
          ryujinx-ldn = pkgs.ryujinx-ldn;
          redis-json = pkgs.redis-json;
        };

        overlays = ldn_overlay;

        # TODO: fully define the module
        nixosModules.ryujinx-ldn = { pkgs, lib, config, ... }: {
          options = let inherit (lib) mkEnableOption mkOption types;
          in {
            services.ryujinx-ldn = {
              enable = mkEnableOption (lib.mdDoc "Ryujinx LDN Server");

              hostname = mkOption {
                type = types.str;
                default = "ldn.ryujinx.org";
                description = lib.mdDoc ''
                  The hostname to use to host Ryujinx LDN.
                '';
              };

              dataPath = mkOption {
                type = types.str;
                default = "/var/lib/ryujinx-ldn";
                description = lib.mdDoc ''
                  The path to the data directory which is used by the LDN website.
                '';
              };

              socketPath = mkOption {
                type = types.str;
                default = "/run/ryujinx-ldn";
                description = lib.mdDoc ''
                  The path to the directory where redis and the LDN website store their sockets in.
                '';
              };

              ldnHost = mkOption {
                type = types.str;
                default = "0.0.0.0";
                description = lib.mdDoc ''
                  The address which the LDN server uses.
                '';
              };

              ldnPort = mkOption {
                type = types.number;
                default = 30456;
                description = lib.mdDoc ''
                  The port which the LDN server exposes over ldnHost.
                '';
              };

              gamelistPath = mkOption {
                type = types.str;
                default = "gamelist.json";
                description = lib.mdDoc ''
                  The path to the json file containing a list of games. This is used by the LDN server.

                  Format of `gamelist.json`:
                  ```json
                  [
                    {
                      "id": "0x<application id of the game>",
                      "name": "<name of the game>"
                    },
                    // ...
                  ]
                  ```
                '';
              };

              nodeEnv = mkOption {
                type = types.enum [ "development" "production" ];
                default = "production";
                description = lib.mdDoc ''
                  The value of the node environment to be set for the LDN website.
                '';
              };

              user = mkOption {
                type = types.str;
                default = "ryujinx-ldn";
                description =
                  lib.mdDoc "User account under which Ryujinx LDN runs.";
              };

              group = mkOption {
                type = types.str;
                default = "ryujinx-ldn";
                description = lib.mdDoc "Group under which Ryujinx LDN runs.";
              };
            };

          };

          config = let
            inherit (lib) mkIf;
            cfg = config.services.ryujinx-ldn;
          in mkIf cfg.enable {
            nixpkgs.overlays = [ self.overlays."${system}" ryujinx-ldn-website.overlays."${system}" ];

            networking.nat.enable = true;

            nixpkgs.config.allowUnfreePredicate = pkg:
              builtins.elem (lib.getName pkg) [ "RedisJSON" ];

            systemd.tmpfiles.rules = [
              "d ${cfg.dataPath} - ${cfg.user} ${cfg.group} -"
              "d ${cfg.socketPath} - ${cfg.user} ${cfg.group} -"
            ];

            services.redis.servers.ryujinx-ldn-stats = let
            in {
              enable = true;
              port = 0;
              save = [ ];
              unixSocket = "${cfg.socketPath}/redis.sock";
              user = cfg.user;
              settings = {
                loadmodule = [ "${pkgs.redis-json}/lib/librejson.so" ];
              };
            };

            systemd.services.ryujinx-ldn-website =
              let website = pkgs.ryujinx-ldn-website;
              in {
                description = "Ryujinx LDN Website";
                after = [ "network.target" ];
                wantedBy = [ "multi-user.target" ];

                environment = {
                  NODE_PATH = "${website}/node_modules";
                  NODE_ENV = cfg.nodeEnv;
                  DATA_PATH = cfg.dataPath;
                  SOCKET_PATH = "${cfg.socketPath}/website.sock";
                  REDIS_SOCKET = "${cfg.socketPath}/redis.sock";
                };

                serviceConfig = rec {
                  Type = "simple";
                  ExecStart = "${website}/bin/ryujinx-ldn-website";
                  User = cfg.user;
                  Group = cfg.group;
                  WorkingDirectory = website;
                  Restart = "on-failure";
                };
              };

            systemd.services.ryujinx-ldn = let ldn = pkgs.ryujinx-ldn;
            in {
              description = "Ryujinx LDN Server";
              after = [ "network.target" ];
              wantedBy = [ "multi-user.target" ];

              environment = {
                LDN_HOST = cfg.ldnHost;
                LDN_PORT = toString cfg.ldnPort;
                LDN_GAMELIST_PATH = cfg.gamelistPath;
                LDN_REDIS_SOCKET = "${cfg.socketPath}/redis.sock";
              };

              serviceConfig = rec {
                Type = "simple";
                ExecStart = "${ldn}/bin/LanPlayServer";
                User = cfg.user;
                Group = cfg.group;
                WorkingDirectory = "${ldn}/lib/${ldn.pname}";
                Restart = "on-failure";
              };
            };

            services.nginx.virtualHosts."${cfg.hostname}" = {
              forceSSL = true;
              enableACME = true;
              locations."/".proxyPass =
                "http://unix:${cfg.socketPath}/website.sock:";
              # TODO: Allow internal traffic to this location
              locations."/info".extraConfig = "return 403;";
            };

            users =
              mkIf (cfg.user == "ryujinx-ldn" && cfg.group == "ryujinx-ldn") {
                users.ryujinx-ldn = {
                  group = cfg.group;
                  isSystemUser = true;
                };
                extraUsers.ryujinx-ldn.uid = 992;

                groups.ryujinx-ldn = { };
                extraGroups.ryujinx-ldn = {
                  name = cfg.group;
                  gid = 990;
                };
              };

            networking.firewall.allowedTCPPorts = [ cfg.ldnPort ];

          };
        };

        checks = {
          vmTest = with import (nixpkgs + "/nixos/lib/testing-python.nix") {
            inherit system;
          };
            makeTest {
              name = "ryujinx-ldn nixos module testing ${system}";

              nodes = {
                client = { ... }: {
                  imports = [ self.nixosModules.${system}.ryujinx-ldn ];

                  services.nginx.enable = true;
                  services.ryujinx-ldn.enable = true;
                  security.acme = {
                    acceptTerms = true;

                    defaults = { email = "dummy@website.com"; };
                  };
                };
              };

              testScript = ''
                start_all()
                client.wait_for_unit("ryujinx-ldn.service")
                client.wait_for_unit("ryujinx-ldn-website.service")
                client.wait_until_succeeds("curl --insecure --fail --header 'Host: ldn.ryujinx.org' https://localhost/api")
              '';
            };

        };

        formatter = pkgs.nixfmt;

      });
}

