/* Icon paths (24x24 viewBox, stroked). The first block is the set used by
 * the design mockup; the rest are the few extras the interpreted screens
 * need, drawn in the same 1.8–2px rounded stroke style. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  BCV.screens = BCV.screens || {}; // screens register here; app.js reads it last

  BCV.IC = {
    dash: 'M4 4h7v7H4zM13 4h7v7h-7zM4 13h7v7H4zM13 13h7v7h-7z',
    book: 'M5 4h13v16H5zM5 17h13M9 8h5',
    people: 'M9 11a3 3 0 100-6 3 3 0 000 6zM3 20c0-3 3-5 6-5s6 2 6 5M16 6a3 3 0 010 6M18 20c0-2-.7-3.4-2-4.3',
    check: 'M4 8l2.5 2.5L11 6M4 17l2.5 2.5L11 15M14 8h6M14 17h6',
    cal: 'M4 7h16v13H4zM4 11h16M8 4v4M16 4v4',
    mail: 'M3 6h18v12H3zM3.6 6.6L12 13.4l8.4-6.8',
    doc: 'M7 3h7l4 4v14H7zM14 3v4h4M10 13h5M10 17h3',
    bolt: 'M13 3L5 14h5l-1 7 8-11h-5z',
    disc: 'M4 5h16v10H9l-5 4z',
    chart: 'M5 20V11M12 20V4M19 20v-6',
    folder: 'M3 7h6l2 2h10v10H3z',
    sheet: 'M4 5h16v14H4zM4 10h16M10 5v14',
    page: 'M6 3h9l3 3v15H6zM9 9h6M9 13h6M9 17h4',
    clock: 'M12 4a8 8 0 100 16 8 8 0 000-16zM12 8v4l3 2',
    bell: 'M6 16v-5a6 6 0 1112 0v5l2 3H4zM10 22h4',
    stream: 'M4 6h16M4 12h10M4 18h13',
    sun: 'M12 6a6 6 0 100 12 6 6 0 000-12zM12 2v2M12 20v2M4 12H2M22 12h-2M5 5l1.5 1.5M17.5 17.5L19 19M19 5l-1.5 1.5M6.5 17.5L5 19',
    moon: 'M20 14a8 8 0 11-9-11 7 7 0 009 11z',
    shield: 'M12 3l8 3v6c0 5-3.5 8-8 9-4.5-1-8-4-8-9V6z',
    // mockup one-offs
    search: 'M11 4a7 7 0 100 14 7 7 0 000-14zM16.5 16.5L21 21',
    chevron: 'M9 6l6 6-6 6',
    back: 'M15 6l-6 6 6 6',
    close: 'M6 6l12 12M18 6L6 18',
    dots: 'M12 6h.01M12 12h.01M12 18h.01',
    compose: 'M4 20h4l11-11-4-4L4 16zM14 5l4 4',
    download: 'M12 4v11M8 11l4 4 4-4M5 19h14',
    reader: 'M11 5H6a2 2 0 00-2 2v10a2 2 0 002 2h5M11 5v14M14 8l3 4-3 4M20 12h-6',
    sparkle: 'M12 3l1.9 4.1L18 9l-4.1 1.9L12 15l-1.9-4.1L6 9l4.1-1.9zM18 15l.9 2.1L21 18l-2.1.9L18 21l-.9-2.1L15 18l2.1-.9z',
    send: 'M12 19V5M6 11l6-6 6 6',
    warn: 'M12 4l9 16H3zM12 10v4M12 17h.01',
    star: 'M12 3.5l2.6 5.6 6.1.7-4.5 4.2 1.2 6-5.4-3-5.4 3 1.2-6L3.3 9.8l6.1-.7z',
    // extras for interpreted screens
    link: 'M10 14a3.5 3.5 0 005 0l4-4a3.5 3.5 0 00-5-5l-1 1M14 10a3.5 3.5 0 00-5 0l-4 4a3.5 3.5 0 005 5l1-1',
    modules: 'M4 6h16M4 12h16M4 18h16M4 6h.01M4 12h.01M4 18h.01',
    external: 'M14 4h6v6M20 4l-9 9M19 14v5H5V5h5',
    plus: 'M12 5v14M5 12h14',
    reply: 'M9 7L4 12l5 5M4 12h11a5 5 0 015 5v2',
    copy: 'M9 9h11v11H9zM5 15V4h11',
    stop: 'M7 7h10v10H7z',
    settings: 'M12 9a3 3 0 100 6 3 3 0 000-6zM4 12h2M18 12h2M12 4v2M12 18v2M6.3 6.3l1.4 1.4M16.3 16.3l1.4 1.4M6.3 17.7l1.4-1.4M16.3 7.7l1.4-1.4',
    image: 'M4 5h16v14H4zM4 15l5-5 4 4 3-3 4 4M15 9h.01',
    video: 'M3 6h13v12H3zM16 10l5-3v10l-5-3',
    audio: 'M12 4v16M8 8v8M16 8v8M4 11v2M20 11v2',
    zip: 'M4 4h16v16H4zM10 4v4M10 10v2M10 14v2',
    calendarPlus: 'M4 7h16v13H4zM4 11h16M8 4v4M16 4v4M12 13v4M10 15h4',
    lock: 'M6 11h12v9H6zM9 11V8a3 3 0 016 0v3',
    filter: 'M4 5h16l-6 8v5l-4 2v-7z',
    pencil: 'M4 20h4l11-11-4-4L4 16zM14 5l4 4',
    text: 'M5 6h14M12 6v14M9 20h6',
  };
})();
