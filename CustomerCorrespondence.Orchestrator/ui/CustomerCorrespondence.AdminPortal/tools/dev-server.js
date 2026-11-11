#!/usr/bin/env node
'use strict';

/**
 * Local dev server for the portal against the real API (npm start).
 *
 * Serves the portal's files (no caching) and forwards /api and /hubs to the API, WebSockets included, so live
 * updates (SignalR) work the way they do when deployed. http-server's proxy can't forward WebSockets: the
 * live connection then hung at "connecting", and nothing live reached the browser (record updates, alerts,
 * "signed out from another browser").
 *
 *   node tools/dev-server.js [--port 8080] [--api https://localhost:7228] [--open]
 */
var http = require('http');
var fs = require('fs');
var path = require('path');
var httpProxy = require('http-proxy');

var args = process.argv.slice(2);
function arg(name, fallback) {
  var i = args.indexOf('--' + name);
  return i !== -1 && args[i + 1] ? args[i + 1] : fallback;
}
var PORT = Number(arg('port', process.env.PORT || 8080));
var API = arg('api', process.env.ECCO_API || 'https://localhost:7228');
var ROOT = path.resolve(__dirname, '..');

var TYPES = {
  '.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8', '.css': 'text/css; charset=utf-8',
  '.json': 'application/json; charset=utf-8', '.map': 'application/json; charset=utf-8', '.svg': 'image/svg+xml',
  '.png': 'image/png', '.jpg': 'image/jpeg', '.jpeg': 'image/jpeg', '.gif': 'image/gif', '.ico': 'image/x-icon',
  '.woff': 'font/woff', '.woff2': 'font/woff2', '.ttf': 'font/ttf', '.pdf': 'application/pdf', '.txt': 'text/plain; charset=utf-8'
};

// The API's development certificate is self-signed, hence secure: false (local development only).
var proxy = httpProxy.createProxyServer({ target: API, secure: false, changeOrigin: true, ws: true, xfwd: true });
proxy.on('error', function (err, req, res) {
  console.error('[proxy] ' + req.method + ' ' + req.url + ': ' + err.message);
  if (res && res.writeHead && !res.headersSent) {
    res.writeHead(502, { 'Content-Type': 'application/json' });
    res.end(JSON.stringify({ message: 'The API at ' + API + " isn't reachable. Is it running?" }));
  } else if (res && res.destroy) {
    res.destroy(); // a WebSocket upgrade: just close it
  }
});

function proxied(url) {
  return /^\/(api|hubs)(\/|$|\?)/.test(url);
}

function serveFile(req, res) {
  var urlPath = decodeURIComponent(req.url.split('?')[0]);
  var file = path.normalize(path.join(ROOT, urlPath === '/' ? 'index.html' : urlPath));
  if (file.indexOf(ROOT) !== 0) { res.writeHead(403); res.end(); return; } // no ../ escapes
  fs.stat(file, function (err, stat) {
    if (!err && stat.isDirectory()) { file = path.join(file, 'index.html'); }
    fs.readFile(file, function (readErr, data) {
      if (readErr) { res.writeHead(404, { 'Content-Type': 'text/plain' }); res.end('Not found'); return; }
      res.writeHead(200, { 'Content-Type': TYPES[path.extname(file).toLowerCase()] || 'application/octet-stream', 'Cache-Control': 'no-store' });
      res.end(data);
    });
  });
}

var server = http.createServer(function (req, res) {
  if (proxied(req.url)) proxy.web(req, res);
  else serveFile(req, res);
});
server.on('upgrade', function (req, socket, head) {
  if (proxied(req.url)) proxy.ws(req, socket, head);
  else socket.destroy();
});

server.listen(PORT, function () {
  var url = 'http://localhost:' + PORT + '/';
  console.log('ECCO portal on ' + url + ' → API ' + API + ' (live updates over WebSockets)');
  if (args.indexOf('--open') !== -1) {
    var cmd = process.platform === 'win32' ? 'start "" "' + url + '"' : process.platform === 'darwin' ? 'open ' + url : 'xdg-open ' + url;
    require('child_process').exec(cmd);
  }
});
