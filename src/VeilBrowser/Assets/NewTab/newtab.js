(() => {
    'use strict';

    const query = new URLSearchParams(location.search);
    const requestedTheme = query.get('theme');
    const theme = ['midnight', 'daylight', 'graphite'].includes(requestedTheme)
        ? requestedTheme
        : 'midnight';
    document.documentElement.classList.add(`theme-${theme}`);

    const hour = new Date().getHours();
    document.querySelector('#greeting').textContent =
        hour < 6 ? '夜深了，隐栈仍在守护你的浏览。' :
        hour < 12 ? '早上好，从一个干净的新标签页开始。' :
        hour < 18 ? '下午好，继续专注而安全地浏览。' :
        '晚上好，守护隐私是美好体验的开始。';

    const input = document.querySelector('#searchInput');
    document.querySelector('#searchForm').addEventListener('submit', (event) => {
        event.preventDefault();
        const value = input.value.trim();
        if (!value) {
            return;
        }

        const looksLikeAddress =
            /^(https?:\/\/|file:\/\/|view-source:)/i.test(value) ||
            (/^[^\s]+\.[^\s]+$/.test(value) && !value.includes(' '));
        location.href = looksLikeAddress
            ? (/^[a-z]+:/i.test(value) ? value : `https://${value}`)
            : `https://www.bing.com/search?q=${encodeURIComponent(value)}`;
    });

    document.querySelector('#focusAddress').addEventListener('click', () => input.focus());
})();
