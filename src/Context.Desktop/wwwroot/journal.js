(() => {
    const observers = new Map();
    function stop(id) {
        observers.get(id)?.disconnect();
        observers.delete(id);
    }
    window.ContextJournal = {
        stop,
        watch(marker, bridge, id) {
            stop(id);
            const observer = new IntersectionObserver(entries => {
                if (observers.get(id) === observer && entries.some(entry => entry.isIntersecting)) {
                    stop(id);
                    bridge.invokeMethodAsync("OnJournalBottom");
                }
            }, { root: marker.closest(".current-region") });
            observers.set(id, observer);
            observer.observe(marker);
        }
    };
})();
