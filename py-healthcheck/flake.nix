{
  description = "Application packaged using poetry2nix";

  inputs = {
    flake-utils.url = "github:numtide/flake-utils";
    nixpkgs.url = "github:NixOS/nixpkgs/nixos-23.11";
    poetry2nix = {
      url = "github:nix-community/poetry2nix";
      inputs.nixpkgs.follows = "nixpkgs";
    };
  };

  outputs = { self, nixpkgs, flake-utils, poetry2nix }@inputs:

    let
      ldn-healthcheck_overlay = final: prev:
        let
          pkgs = import nixpkgs { system = prev.system; };
          poetry2nix = inputs.poetry2nix.lib.mkPoetry2Nix { inherit pkgs; };
        in {
          ldn-healthcheck = with final;
            poetry2nix.mkPoetryApplication rec {
              projectDir = self;

              overrides = poetry2nix.overrides.withDefaults (self: super: {
                "discord-webhook" = super."discord-webhook".overridePythonAttrs
                  (old: {
                    buildInputs = old.buildInputs or [ ]
                      ++ [ python311Packages.poetry-core ];
                  });
              });
            };
        };
    in flake-utils.lib.eachDefaultSystem (system:
      let
        pkgs = import nixpkgs {
          inherit system;
          overlays = [ self.overlays."${system}" ];
        };
      in {
        packages = {
          default = self.packages.${system}.ldn-healthcheck;
          ldn-healthcheck = pkgs.ldn-healthcheck;
        };

        overlays = ldn-healthcheck_overlay;

        nixosModules.ldn-healthcheck = { pkgs, lib, config, ... }: {
          options = let inherit (lib) mkEnableOption mkOption types;
          in {
            services.ldn-healthcheck = {
              enable =
                mkEnableOption (lib.mdDoc "LDN healthcheck script (RyuDoctor)");
              user = mkOption {
                type = types.str;
                default = "ldn-healthcheck";
                description = lib.mdDoc ''
                  User account used to run the ldn-healthcheck script.
                '';
              };
              group = mkOption {
                type = types.str;
                default = "ldn-healthcheck";
                description = lib.mdDoc ''
                  Name of the group used to run the ldn-healthcheck script.
                '';
              };
              ldnServiceName = mkOption {
                type = types.str;
                default = "ryujinx-ldn.service";
                description = lib.mdDoc ''
                  The name of the service the healthcheck script should restart.
                '';
              };
              discordWebhookUrl = mkOption {
                type = types.str;
                description = lib.mdDoc ''
                  The URL of the discord webhook that should be used by the healthcheck script.
                '';
              };
              schedule = mkOption {
                type = types.str;
                default = "*:0/15:00";
                description = lib.mdDoc ''
                  Run the healthcheck script according to the specified schedule (formatted as a systemd calendar event).
                '';
              };
              enableDebug = mkEnableOption (lib.mdDoc "Enable debug logging");
            };
          };

          config = let
            inherit (lib) mkIf;
            cfg = config.services.ldn-healthcheck;
          in mkIf cfg.enable {
            nixpkgs.overlays = [ self.overlays."${system}" ];

            systemd.timers.ldn-healthcheck = {
              description = "Scheduled LDN healthcheck script runs";
              wantedBy = [ "timers.target" ];

              timerConfig = {
                Unit = "ldn-healthcheck.service";
                OnCalendar = cfg.schedule;
                Persistent = true;
              };
            };

            systemd.services.ldn-healthcheck = {
              description = "LDN healthcheck script (RyuDoctor)";
              after = [ "network.target" ];
              script = ''
                ${pkgs.ldn-healthcheck.dependencyEnv}/bin/ldn_healthcheck
              '';

              environment = {
                LDN_SERVICE = cfg.ldnServiceName;
                DC_WEBHOOK = cfg.discordWebhookUrl;
              } // (if cfg.enableDebug then { DEBUG = "1"; } else { });

              serviceConfig = rec {
                Type = "oneshot";
                User = cfg.user;
                Group = cfg.group;
              };
            };

            systemd.services.ldn-healthcheck-debug = mkIf (cfg.enableDebug) {
              description = "LDN healthcheck debug script";
              after = [ "network.target" ];
              script = ''
                ${pkgs.ldn-healthcheck.dependencyEnv}/bin/ldn_healthcheck_debug
              '';

              environment = {
                LDN_SERVICE = cfg.ldnServiceName;
                DEBUG = "1";
              };

              serviceConfig = rec {
                Type = "oneshot";
                User = cfg.user;
                Group = cfg.group;
              };
            };

            users = mkIf (cfg.user == "ldn-healthcheck" && cfg.group
              == "ldn-healthcheck") {
                users.ldn-healthcheck = {
                  group = cfg.group;
                  isSystemUser = true;
                };
                extraUsers.ldn-healthcheck.uid = 993;

                groups.ldn-healthcheck = { };
                extraGroups.ldn-healthcheck = {
                  name = cfg.group;
                  gid = 994;
                };
              };
          };
        };

        devShells.default = pkgs.mkShell {
          inputsFrom = [ self.packages.${system}.ldn-healthcheck ];
          packages = [ pkgs.poetry ];
        };

        formatter = pkgs.nixfmt;
      });
}
