/* The page after install: the shared stylesheet, applied before the first paint. */
document.head.append(Object.assign(document.createElement('style'), { textContent: self.BCV_SETUP_CSS }));
