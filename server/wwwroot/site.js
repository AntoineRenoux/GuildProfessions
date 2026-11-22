// Guildkeep — comportements du site. Tout le contenu reste lisible sans script (français par défaut).
(() => {
	const root = document.documentElement;

	// ---- Langue : FR / EN, mémorisée pour ce navigateur ------------------------------------
	const setLang = (lang) => {
		root.lang = lang;
		try { localStorage.setItem('gk-lang', lang); } catch (e) { /* stockage refusé : la langue vaut pour cette page */ }
		document.querySelectorAll('[data-set-lang]').forEach((b) => b.setAttribute('aria-pressed', String(b.dataset.setLang === lang)));
		document.dispatchEvent(new CustomEvent('gk:lang'));
	};
	document.querySelectorAll('[data-set-lang]').forEach((b) => b.addEventListener('click', () => setLang(b.dataset.setLang)));
	setLang(root.lang === 'en' ? 'en' : 'fr');
	const T = (fr, en) => (root.lang === 'en' ? en : fr);

	// ---- Lien d'invitation du bot, fourni par le serveur ----------------------------------------
	fetch('/api/v1/site').then((r) => (r.ok ? r.json() : null)).then((site) => {
		if (!site || !site.inviteUrl) return;
		document.querySelectorAll('.invite-link').forEach((a) => { a.href = site.inviteUrl; a.rel = 'noopener'; });
	}).catch(() => {});

	// ---- Hero : la recherche « qui sait le faire ? » sur une guilde d'exemple ------------------
	const PROFESSIONS = [
		{ id: 'forge', fr: 'Forge', en: 'Blacksmithing', art: 'forge', alt: ['le forgeron', 'the blacksmith'], crafters: [['Bragg Ferrefond', 300, 'dispo le soir, fournir les barres', 'evenings, bring the bars'], ['Hilde Marteau', 262, 'armes surtout', 'mostly weapons'], ['Ondrin Cendre', 190, '', '']] },
		{ id: 'cuir', fr: 'Travail du cuir', en: 'Leatherworking', art: 'cuir', alt: ['le corroyeur', 'the leatherworker'], crafters: [['Sorcha Tanne', 300, 'cuir épais fourni', 'heavy leather provided'], ['Kael Ronce', 241, '', '']] },
		{ id: 'couture', fr: 'Couture', en: 'Tailoring', art: 'couture', alt: ['le tailleur', 'the tailor'], crafters: [['Ysolde Brume', 300, 'robes runiques', 'runecloth robes'], ['Mireille Fil', 275, 'sacs 16 places', '16-slot bags'], ['Tobias Lin', 120, '', '']] },
		{ id: 'ingenierie', fr: 'Ingénierie', en: 'Engineering', art: 'ingenierie', alt: ["l'horloger", 'the clockmaker'], crafters: [['Tamsin Rouage', 300, 'lunettes et explosifs', 'goggles and explosives'], ['Pip Vapeur', 210, '', '']] },
		{ id: 'alchimie', fr: 'Alchimie', en: 'Alchemy', art: 'alchimie', alt: ["l'apothicaire", 'the apothecary'], crafters: [['Maël Corvin', 300, 'transmutation arcanite', 'arcanite transmute'], ['Elwin Sauge', 265, 'flacons sur demande', 'flasks on request'], ['Nora Fiel', 230, '', '']] },
		{ id: 'enchantement', fr: 'Enchantement', en: 'Enchanting', art: 'enchantement', alt: ["l'astronome", 'the astronomer'], crafters: [['Aurèle Vesper', 300, 'arme +15 agilité', 'weapon +15 agility'], ['Liv Astrale', 245, '', '']] },
		{ id: 'minage', fr: 'Minage', en: 'Mining', art: null, alt: ['le mineur', 'the miner'], crafters: [['Dunrik Galet', 300, 'thorium au kilo', 'thorium by the stack'], ['Ilsa Roche', 180, '', '']] },
		{ id: 'herboristerie', fr: 'Herboristerie', en: 'Herbalism', art: null, alt: ['le paysan', 'the farmer'], crafters: [['Elwin Sauge', 286, 'lotus noir parfois', 'black lotus sometimes'], ['Faye Ortie', 150, '', '']] },
		{ id: 'depecage', fr: 'Dépeçage', en: 'Skinning', art: null, alt: ['le pelletier', 'the furrier'], crafters: [['Sorcha Tanne', 300, '', ''], ['Kael Ronce', 230, '', '']] },
		{ id: 'cuisine', fr: 'Cuisine', en: 'Cooking', art: null, alt: ['le cuisinier', 'the cook'], crafters: [['Bragg Ferrefond', 285, 'festins pour les raids', 'raid feasts'], ['Mireille Fil', 225, '', '']] },
		{ id: 'secourisme', fr: 'Secourisme', en: 'First Aid', art: null, alt: ['le barbier-chirurgien', 'the barber-surgeon'], crafters: [['Nora Fiel', 300, 'bandages d’étoffe runique', 'runecloth bandages']] },
		{ id: 'peche', fr: 'Pêche', en: 'Fishing', art: null, alt: ['le pêcheur', 'the fisherman'], crafters: [['Pip Vapeur', 300, 'poissons de raid', 'raid fish'], ['Tobias Lin', 140, '', '']] },
	];
	const select = document.getElementById('chip-prof');
	const level = document.getElementById('chip-level');
	const levelOut = document.getElementById('chip-level-out');
	const img = document.getElementById('answer-img');
	const plateName = document.getElementById('answer-plate-name');
	const rows = document.getElementById('answer-rows');
	const cmd = document.getElementById('answer-cmd');
	const embed = document.getElementById('answer-embed');
	const esc = (s) => s.replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));

	const renderAnswer = () => {
		if (!select) return;
		const prof = PROFESSIONS.find((p) => p.id === select.value) || PROFESSIONS[0];
		const min = Number(level.value);
		levelOut.textContent = String(min);
		const name = T(prof.fr, prof.en);
		// Pas de gravure pour ce métier : son nom, en grand, sur le même fond.
		img.hidden = !prof.art;
		plateName.hidden = Boolean(prof.art);
		plateName.textContent = name;
		if (prof.art) {
			img.src = `/img/metiers/${prof.art}.jpg`;
			img.alt = T(`Gravure : ${prof.alt[0]}, Ständebuch de Jost Amman, 1568`, `Woodcut: ${prof.alt[1]}, Jost Amman's Ständebuch, 1568`);
		}
		cmd.textContent = T(`/gkeep metier qui metier:${prof.fr} niveau_min:${min}`, `/gkeep profession who profession:${prof.en} min_level:${min}`);
		const found = prof.crafters.filter((c) => c[1] >= min);
		embed.classList.toggle('amber', found.length === 0);
		rows.innerHTML = found.length
			? found.map((c) => `<div class="field"><b>${esc(c[0])} · ${esc(name)} ${c[1]}/300</b>${esc(T(c[2], c[3]) || T('pas de note', 'no note'))}</div>`).join('')
			: `<div class="field">${esc(T(`Personne en ${name} à ${min} ou plus. Baisse le niveau minimum.`, `Nobody in ${name} at ${min} or above. Lower the minimum level.`))}</div>`;
	};
	const fillSelect = () => {
		if (!select) return;
		const current = select.value || 'forge';
		select.innerHTML = PROFESSIONS.map((p) => `<option value="${p.id}">${esc(T(p.fr, p.en))}</option>`).join('');
		select.value = current;
		renderAnswer();
	};
	if (select) {
		fillSelect();
		select.addEventListener('change', renderAnswer);
		level.addEventListener('input', renderAnswer);
		document.addEventListener('gk:lang', fillSelect);
	}

	// ---- Vitrine : seul le fragment centré est éclairé -------------------------------------------
	const track = document.getElementById('track');
	if (track) {
		const slides = [...track.querySelectorAll('.slide')];
		const light = () => {
			const mid = track.getBoundingClientRect().left + track.clientWidth / 2;
			let best = slides[0];
			let dist = Infinity;
			slides.forEach((s) => {
				const r = s.getBoundingClientRect();
				const d = Math.abs(r.left + r.width / 2 - mid);
				if (d < dist) { dist = d; best = s; }
			});
			slides.forEach((s) => s.classList.toggle('is-lit', s === best));
		};
		let frame = 0;
		track.addEventListener('scroll', () => { cancelAnimationFrame(frame); frame = requestAnimationFrame(light); }, { passive: true });
		window.addEventListener('resize', light);
		light();
		// Centre un fragment en ne faisant défiler que la rangée, jamais la page.
		const center = (slide, smooth) => {
			const left = slide.offsetLeft - (track.clientWidth - slide.offsetWidth) / 2;
			track.scrollTo({ left, behavior: smooth && !matchMedia('(prefers-reduced-motion: reduce)').matches ? 'smooth' : 'auto' });
		};
		const step = (dir) => {
			const lit = slides.findIndex((s) => s.classList.contains('is-lit'));
			center(slides[Math.max(0, Math.min(slides.length - 1, lit + dir))], true);
		};
		document.querySelectorAll('[data-track]').forEach((b) => b.addEventListener('click', () => step(Number(b.dataset.track))));
		track.addEventListener('keydown', (e) => {
			if (e.key === 'ArrowRight') { e.preventDefault(); step(1); }
			if (e.key === 'ArrowLeft') { e.preventDefault(); step(-1); }
		});
		// Glisser à la souris.
		let drag = null;
		track.addEventListener('pointerdown', (e) => { if (e.pointerType === 'mouse') drag = { x: e.clientX, left: track.scrollLeft }; });
		window.addEventListener('pointermove', (e) => { if (drag) track.scrollLeft = drag.left - (e.clientX - drag.x); });
		window.addEventListener('pointerup', () => { drag = null; });
		// Démarre sur le deuxième fragment : un voisin visible de chaque côté.
		requestAnimationFrame(() => { if (slides[1]) center(slides[1], false); light(); });
	}

	// ---- Planche : notes alignées sur la partie qu'elles décrivent, survol qui l'isole -----------
	const plate = document.getElementById('plate');
	if (plate) {
		const align = () => {
			const wide = window.innerWidth > 1100;
			plate.classList.toggle('is-aligned', wide);
			// Chaque note se cale sur sa partie, sans chevaucher la précédente ni le titre.
			const heading = plate.querySelector('h2');
			plate.querySelectorAll('.notes').forEach((column) => {
				const box = column.getBoundingClientRect();
				let floor = column.classList.contains('notes-left') ? heading.getBoundingClientRect().bottom - box.top + 32 : 0;
				column.querySelectorAll('.note').forEach((note) => {
					if (!wide) { note.style.top = ''; return; }
					const part = plate.querySelector(`[data-part="${note.dataset.for}"]`);
					const top = Math.max(part.getBoundingClientRect().top - box.top, floor);
					note.style.top = `${top}px`;
					floor = top + note.offsetHeight + 20;
				});
			});
			const tallest = Math.max(...[...plate.querySelectorAll('.notes')].map((n) => n.scrollHeight));
			plate.querySelectorAll('.notes').forEach((n) => { n.style.minHeight = wide ? `${tallest}px` : ''; });
		};
		const focus = (note, on) => {
			plate.classList.toggle('is-focus', on);
			plate.querySelectorAll('[data-part]').forEach((p) => p.classList.toggle('is-on', on && p.dataset.part === note.dataset.for));
		};
		plate.querySelectorAll('.note').forEach((note) => {
			note.addEventListener('mouseenter', () => focus(note, true));
			note.addEventListener('mouseleave', () => focus(note, false));
			note.addEventListener('focusin', () => focus(note, true));
			note.addEventListener('focusout', () => focus(note, false));
		});
		window.addEventListener('resize', align);
		document.addEventListener('gk:lang', align);
		if (document.fonts) document.fonts.ready.then(align); else align();
		window.addEventListener('load', align);
	}

	// ---- Commandes : onglets ---------------------------------------------------------------------
	const tabs = [...document.querySelectorAll('.tabs [role="tab"]')];
	tabs.forEach((tab) => tab.addEventListener('click', () => {
		tabs.forEach((t) => {
			const on = t === tab;
			t.setAttribute('aria-selected', String(on));
			document.getElementById(t.getAttribute('aria-controls')).hidden = !on;
		});
	}));

	// ---- Rejoindre : un code ou un lien complet mène à la page de la guilde -------------------------
	const form = document.getElementById('join-form');
	if (form) {
		form.addEventListener('submit', (e) => {
			e.preventDefault();
			const raw = document.getElementById('join-code').value.trim();
			const code = (raw.match(/([a-z0-9]{6,32})\/?$/i) || [])[1];
			const error = document.getElementById('join-error');
			if (!code) {
				error.textContent = T('Colle le lien partagé par ton administrateur, ou seulement sa fin (des lettres et des chiffres).', 'Paste the link your admin shared, or just its end (letters and digits).');
				return;
			}
			window.location.href = `/rejoindre/${code.toLowerCase()}`;
		});
	}

	// ---- Entrées des sections, une fois ----------------------------------------------------------
	const targets = document.querySelectorAll('[data-reveal]');
	if (!('IntersectionObserver' in window)) { targets.forEach((el) => el.classList.add('is-in')); return; }
	const io = new IntersectionObserver((entries) => {
		entries.forEach((entry) => {
			if (!entry.isIntersecting) return;
			entry.target.classList.add('is-in');
			io.unobserve(entry.target);
		});
	}, { rootMargin: '0px 0px -10% 0px', threshold: 0.08 });
	targets.forEach((el, i) => { el.style.setProperty('--i', String(i % 3)); io.observe(el); });
	window.addEventListener('load', () => setTimeout(() => {
		targets.forEach((el) => { const r = el.getBoundingClientRect(); if (r.top < innerHeight && r.bottom > 0) el.classList.add('is-in'); });
	}, 2500));
})();
