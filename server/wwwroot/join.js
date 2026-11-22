// Page /rejoindre/{code} : nom de la guilde, pack du bon système en premier, étapes du système.
(() => {
	const T = (fr, en) => (document.documentElement.lang === 'en' ? en : fr);
	const code = (location.pathname.match(/\/(?:rejoindre|join)\/([a-z0-9]{6,32})\/?$/i) || [])[1];

	const detectOs = () => {
		const ua = navigator.userAgent;
		if (/Windows/i.test(ua)) return 'win';
		if (/Mac OS X|Macintosh/i.test(ua)) return 'mac';
		if (/Linux|X11/i.test(ua) && !/Android/i.test(ua)) return 'linux';
		return 'win';
	};
	const os = detectOs();

	// Onglets des étapes, ouverts sur le système détecté.
	const tabs = [...document.querySelectorAll('[data-os]')];
	const showOs = (id) => {
		tabs.forEach((t) => t.setAttribute('aria-selected', String(t.dataset.os === id)));
		document.querySelectorAll('[data-os-panel]').forEach((p) => { p.hidden = p.dataset.osPanel !== id; });
	};
	tabs.forEach((t) => t.addEventListener('click', () => showOs(t.dataset.os)));
	showOs(os);

	const primary = document.getElementById('primary-download');
	const others = document.getElementById('other-downloads');
	const missing = () => {
		document.getElementById('missing').hidden = false;
		document.getElementById('downloads').hidden = true;
		document.getElementById('lead').hidden = true;
	};
	if (!code) { missing(); return; }

	// Mac : Apple Silicon par défaut (les Mac Intel ne sont plus vendus depuis 2023).
	const preferred = { win: 'win-x64', mac: 'osx-arm64', linux: 'linux-x64' }[os];
	let guild = null;

	const render = () => {
		if (!guild) return;
		const name = document.getElementById('guild-name');
		name.textContent = guild.name;
		document.title = T(`Guildkeep · rejoindre ${guild.name}`, `Guildkeep · join ${guild.name}`);
		const lead = document.getElementById('lead');
		lead.textContent = T(
			`Le pack de ${guild.name} : l'addon Guildkeep et son compagnon, déjà réglés pour la guilde. Télécharge-le, lance-le, c'est fini.`,
			`${guild.name}'s pack: the Guildkeep addon and its companion, already set up for the guild. Download it, run it, done.`);
		const label = (p) => ({ 'win-x64': 'Windows', 'osx-arm64': T('Mac (puce Apple)', 'Mac (Apple chip)'), 'osx-x64': 'Mac (Intel)', 'linux-x64': 'Linux' }[p.id] || p.label);
		const main = guild.platforms.find((p) => p.id === preferred) || guild.platforms[0];
		if (!main) {
			primary.textContent = T('Pack bientôt disponible', 'Pack coming soon');
			primary.removeAttribute('href');
			return;
		}
		primary.href = main.url;
		primary.textContent = T(`Télécharger pour ${label(main)}`, `Download for ${label(main)}`);
		others.innerHTML = '';
		const intro = document.createElement('span');
		intro.textContent = T('Autre système :', 'Other system:');
		intro.style.color = 'var(--muted)';
		others.append(intro);
		guild.platforms.filter((p) => p !== main).forEach((p) => {
			const a = document.createElement('a');
			a.className = 'link';
			a.href = p.url;
			a.textContent = label(p);
			a.style.textDecorationColor = 'currentColor';
			others.append(a);
		});
	};

	fetch(`/api/v1/join/${code}`)
		.then((r) => (r.ok ? r.json() : Promise.reject(r.status)))
		.then((data) => { guild = data; render(); })
		.catch(missing);
	document.addEventListener('gk:lang', render);
})();
