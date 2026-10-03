// The API serves the WebSocket on port 5080 in development.
// Behind HTTPS (deployment) the browser requires wss://, so we pick the scheme from the page.
const scheme = location.protocol === 'https:' ? 'wss' : 'ws';

export const WS_URL = `${scheme}://${location.hostname}:5080/ws`;

// Map tiles. OpenStreetMap's standard tiles need no key and are fine for development and light
// personal use (see https://operations.osmfoundation.org/policies/tiles/). For a public
// deployment with real traffic, switch to a provider you have an account or key for.
export const TILES = {
  url: 'https://tile.openstreetmap.org/{z}/{x}/{y}.png',
  attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors',
  maxZoom: 19,
};