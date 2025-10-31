const express = require('express');
const path = require('path');

const app = express();
const PORT = process.env.PORT || 3000;

// Serve static files from the current directory
app.use(express.static(__dirname));

// Route for the main page
app.get('/', (req, res) => {
  res.sendFile(path.join(__dirname, 'landing.html'));
});

// Route to serve the source code file
app.get('/ClipAid-Pro.cs', (req, res) => {
  res.sendFile(path.join(__dirname, 'ClipAid-Pro.cs'));
});

// Route to serve the executable
app.get('/ClipAid-Pro.exe', (req, res) => {
  res.sendFile(path.join(__dirname, 'ClipAid-Pro.exe'));
});

// SEO routes
app.get('/sitemap.xml', (req, res) => {
  res.type('application/xml');
  res.sendFile(path.join(__dirname, 'sitemap.xml'));
});

app.get('/robots.txt', (req, res) => {
  res.type('text/plain');
  res.sendFile(path.join(__dirname, 'robots.txt'));
});

// 404 handler
app.use((req, res) => {
  res.status(404).send('Not Found');
});

app.listen(PORT, () => {
  console.log(`ClipAid landing page running on port ${PORT}`);
});
