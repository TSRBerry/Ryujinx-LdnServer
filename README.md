```
__________                     __ .__                  .____         .___        
\______   \ ___.__. __ __     |__||__|  ____  ___  ___ |    |      __| _/  ____  
 |       _/<   |  ||  |  \    |  ||  | /    \ \  \/  / |    |     / __ |  /    \ 
 |    |   \ \___  ||  |  /    |  ||  ||   |  \ >    <  |    |___ / /_/ | |   |  \
 |____|_  / / ____||____/ /\__|  ||__||___|  //__/\_ \ |_______ \\____ | |___|  /
        \/  \/            \______|         \/       \/         \/     \/      \/ 
```

# Ryujinx LDN Multiplayer Server

This server drives multiplayer "matchmaking" in Ryujinx's local wireless implementation. Players can create "networks" on the server, which can then be scanned and found by other players as if they were within that network's range. The server entirely manages the network information and player join/leave lifecycle for all games created.

## License

This software is licensed under the terms of the [MIT license](LICENSE.txt).

## Proxy

Ryujinx uses an application layer proxy to simulate communications in a network, which directs traffic created by the C# socket to a proxy server, which routes and sends it to the relevant parties. By default, Ryujinx attempts to create a proxy server on the network owner's system, expose a port to the internet, and tell joiners to connect to it. If this does not work, the proxy is done through the master server itself, using a lot of the same mechanisms.

## External Analytics API via Http Server

- `ldn.ryujinx.org:API_PORT/api`<br />
  This provide global analytics over the whole server.<br />
  E.g.:<br />
  ```json
  {
      "total_game_count": 4,
      "private_game_count": 1,
      "public_game_count": 3,
      "in_progress_count": 0,
      "master_proxy_count": 0,
      "total_player_count": 10,
      "private_player_count": 3,
      "public_player_count": 7
  }
  ```
  
- `ldn.ryujinx.org:API_PORT/api/public_games`<br />
  This provide detailed analytics over all games. Only public games are available.<br />
  E.g.:<br />
  ```json
  [
      {
          "id": "d2d0244c2b4544d7a26cd051e0e993e9",
          "player_count": 3,
          "max_player_count": 4,
          "game_name": "Puyo Puyo Tetris",
          "title_id": "010053d0001be000",
          "mode": "P2P",
          "status": "Joinable",
          "players": [
              "Player1",
              "Player2",
              "Player3"
          ]
      }
  ]
  ```
  
- `ldn.ryujinx.org:API_PORT/api/public_games?titleid=TITLEID`<br />
  This provide detailed analytics over a specific game, using the title id. Only public games are available.<br />
  E.g.:<br />
  ```json
  [
      {
          "id": "d2d0244c2b4544d7a26cd051e0e993e9",
          "player_count": 3,
          "max_player_count": 4,
          "game_name": "Puyo Puyo Tetris",
          "title_id": "010053d0001be000",
          "mode": "P2P",
          "status": "Joinable",
          "players": [
              "Player1",
              "Player2",
              "Player3"
          ]
      }
  ]
  ```

## Management

(not implemented yet)

The console is listening for commands while the server is running. You can do any of the following:

- `!restart-ldn`<br /> 
  Restart the LDN server.
- `!restart-api`<br /> 
  Restart the API server.
- `!list`<br /> 
  Lists all games and connected users. You will also be able to see passphrase games.
- `!close` - Closes LDN and API servers.