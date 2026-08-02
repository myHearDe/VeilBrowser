(() => {
    'use strict';

    const APP_HANDLER = 'app';
    const query = new URLSearchParams(location.search);
    const targetUrl = query.get('target') || '';
    const requestedTheme = query.get('theme');
    const theme = ['midnight', 'daylight', 'graphite'].includes(requestedTheme)
        ? requestedTheme
        : 'midnight';
    document.documentElement.classList.add(`theme-${theme}`);
    let currentTab = null;
    let currentInfo = null;

    const elements = {
        extensionState: document.querySelector('#extensionState'),
        statusDot: document.querySelector('#statusDot'),
        siteName: document.querySelector('#siteName'),
        siteAddress: document.querySelector('#siteAddress'),
        pageBlocked: document.querySelector('#pageBlocked'),
        totalBlocked: document.querySelector('#totalBlocked'),
        siteProtectionText: document.querySelector('#siteProtectionText'),
        siteToggle: document.querySelector('#siteToggle'),
        pickElement: document.querySelector('#pickElement'),
        message: document.querySelector('#message'),
        retry: document.querySelector('#retry'),
    };

    const send = (type, data) => chrome.runtime.sendMessage({
        handlerName: APP_HANDLER,
        type,
        data,
    });

    const normalizeUrl = (value) => {
        try {
            const url = new URL(value);
            url.hash = '';
            return url.href.replace(/\/$/, '');
        } catch {
            return value.replace(/\/$/, '');
        }
    };

    const setMessage = (text, kind = '') => {
        elements.message.textContent = text;
        elements.message.className = kind;
    };

    const formatCount = (value) => {
        const number = Number(value);
        return Number.isFinite(number) ? number.toLocaleString('zh-CN') : '0';
    };

    async function findTargetTab() {
        const tabs = await chrome.tabs.query({});
        const normalizedTarget = normalizeUrl(targetUrl);
        const exact = tabs.find((tab) =>
            normalizeUrl(tab.url || tab.pendingUrl || '') === normalizedTarget);
        if (exact) {
            return exact;
        }

        return tabs
            .filter((tab) => /^https?:/i.test(tab.url || tab.pendingUrl || ''))
            .sort((a, b) => (b.lastAccessed || 0) - (a.lastAccessed || 0))[0] || null;
    }

    function renderInfo() {
        const frame = currentInfo?.frameInfo;
        const filteringPaused = Boolean(frame?.applicationFilteringDisabled);
        const allowlisted = Boolean(frame?.documentAllowlisted || frame?.userAllowlisted);
        const filteringPossible = frame?.isFilteringPossible !== false;
        const protectedOnSite = filteringPossible && !filteringPaused && !allowlisted;

        elements.siteName.textContent = frame?.domainName || currentTab?.title || '当前页面';
        elements.siteAddress.textContent = frame?.url || currentTab?.url || targetUrl || '未找到网页标签页';
        elements.pageBlocked.textContent = formatCount(frame?.totalBlockedTab);
        elements.totalBlocked.textContent = formatCount(frame?.totalBlocked);
        elements.siteToggle.disabled = !filteringPossible || filteringPaused || !currentTab?.id;
        elements.siteToggle.setAttribute('aria-pressed', String(protectedOnSite));
        elements.pickElement.disabled = !filteringPossible || filteringPaused ||
            allowlisted || !currentTab?.id;

        if (!filteringPossible) {
            elements.siteProtectionText.textContent = '此页面不支持过滤';
        } else if (filteringPaused) {
            elements.siteProtectionText.textContent = 'AdGuard 全局防护已暂停';
        } else if (allowlisted) {
            elements.siteProtectionText.textContent = '已加入允许列表';
        } else {
            elements.siteProtectionText.textContent = '广告与跟踪器拦截已开启';
        }
    }

    async function refresh() {
        elements.siteToggle.disabled = true;
        elements.pickElement.disabled = true;
        elements.extensionState.textContent = '正在连接保护引擎…';
        elements.statusDot.classList.remove('ready');
        setMessage('');

        try {
            currentTab = await findTargetTab();
            if (!currentTab?.id) {
                throw new Error('没有找到可控制的网页标签页。');
            }

            currentInfo = await send('getTabInfoForPopup', { tabId: currentTab.id });
            if (!currentInfo?.frameInfo) {
                throw new Error('AdGuard 尚未完成页面初始化，请刷新网页后重试。');
            }

            renderInfo();
            elements.extensionState.textContent = '保护引擎已连接';
            elements.statusDot.classList.add('ready');
        } catch (error) {
            elements.siteName.textContent = '无法读取当前页面';
            elements.siteAddress.textContent = targetUrl || '请先打开一个网页';
            elements.siteProtectionText.textContent = '连接失败';
            setMessage(error?.message || String(error), 'error');
        }
    }

    elements.siteToggle.addEventListener('click', async () => {
        if (!currentTab?.id || !currentInfo?.frameInfo) {
            return;
        }

        elements.siteToggle.disabled = true;
        const allowlisted = Boolean(
            currentInfo.frameInfo.documentAllowlisted ||
            currentInfo.frameInfo.userAllowlisted);
        try {
            if (allowlisted) {
                await send('removeAllowlistDomain', {
                    tabId: currentTab.id,
                    tabRefresh: true,
                });
                setMessage('已恢复当前网站防护，页面正在刷新。', 'success');
            } else {
                await send('addAllowlistDomainForTabId', { tabId: currentTab.id });
                setMessage('已暂停当前网站防护。', 'success');
            }
            window.setTimeout(refresh, 500);
        } catch (error) {
            setMessage(`切换失败：${error?.message || error}`, 'error');
            elements.siteToggle.disabled = false;
        }
    });

    elements.pickElement.addEventListener('click', async () => {
        if (!currentTab?.id) {
            return;
        }

        elements.pickElement.disabled = true;
        setMessage('正在网页中启动元素选择器…');
        try {
            await send('openAssistant', { tabId: currentTab.id });
            setMessage('元素选择器已启动，请回到网页并点击要屏蔽的内容。', 'success');
            if (globalThis.chrome?.webview?.postMessage) {
                chrome.webview.postMessage('close');
            }
        } catch (error) {
            setMessage(`元素选择器启动失败：${error?.message || error}`, 'error');
            elements.pickElement.disabled = false;
        }
    });

    document.querySelectorAll('[data-page]').forEach((button) => {
        button.addEventListener('click', () => {
            const page = button.dataset.page;
            window.open(chrome.runtime.getURL(`pages/${page}`));
        });
    });

    elements.retry.addEventListener('click', refresh);
    refresh();
})();
