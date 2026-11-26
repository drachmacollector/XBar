// Raises WhatsApp Web's message composer (the <footer> in the open chat) so a floating
// XBar taskbar doesn't cover it. The page itself still fills the whole window.
// Only active when the browser window actually reaches the bottom of a screen that has
// no reserved taskbar strip (i.e. XBar overlay mode); otherwise it does nothing.
(() => {
  const TASKBAR_PX = 64;   // how far to lift: pill height + float gap, in CSS px. Raise it if the box is still covered.
  const STYLE_ID = "xbar-whatsapp-lift";

  function lift() {
    const reservedStrip = screen.height - screen.availHeight;           // >0 means Windows already reserves the taskbar
    const touchesBottom = window.screenY + window.outerHeight >= screen.height - 4;
    const px = reservedStrip === 0 && touchesBottom ? TASKBAR_PX : 0;

    let style = document.getElementById(STYLE_ID);
    if (!style) {
      style = document.createElement("style");
      style.id = STYLE_ID;
      document.head.appendChild(style);
    }
    style.textContent = px
      ? `#main footer, #app footer { margin-bottom: ${px}px !important; }`
      : "";
  }

  lift();
  window.addEventListener("resize", lift);
  setInterval(lift, 3000);   // window moved between monitors / overlay toggled
})();
