// The command bar page (job 282), hand-written, no packages. Every shown text is set through textContent. Messages go
// to the host as JSON: complete (each change of the line), run (Enter, the line as typed) and close (Esc, or the window
// losing focus). Tab puts the picked suggestion in the line; Up and Down move the pick.
(function () {
    'use strict';

    var line = document.getElementById('line');
    var list = document.getElementById('suggestions');
    var result = document.getElementById('result');
    var items = [];
    var picked = 0;
    var closed = false;

    function send(message) {
        if (window.external && typeof window.external.sendMessage === 'function') {
            window.external.sendMessage(JSON.stringify(message));
        }
    }

    function close() {
        if (closed) {
            return;
        }

        closed = true;
        send({ type: 'close' });
    }

    function showResult(kind, text) {
        result.className = kind === 'ok' ? 'ok' : (kind === 'error' ? 'error' : '');
        result.textContent = text;
    }

    function draw() {
        while (list.firstChild) {
            list.removeChild(list.firstChild);
        }

        for (var i = 0; i < items.length; i++) {
            var row = document.createElement('li');
            row.setAttribute('role', 'option');
            row.setAttribute('aria-selected', i === picked ? 'true' : 'false');
            var label = document.createElement('span');
            label.className = 'label';
            label.textContent = items[i].label;
            var detail = document.createElement('span');
            detail.className = 'detail';
            detail.textContent = items[i].detail;
            row.appendChild(label);
            row.appendChild(detail);
            list.appendChild(row);
        }

        line.setAttribute('aria-expanded', items.length > 0 ? 'true' : 'false');
    }

    function receive(json) {
        var message;
        try {
            message = JSON.parse(json);
        } catch (e) {
            return;
        }

        if (!message || typeof message.type !== 'string') {
            return;
        }

        if (message.type === 'suggestions' && Array.isArray(message.items)) {
            items = message.items.filter(function (item) {
                return item && typeof item.line === 'string' && typeof item.label === 'string' && typeof item.detail === 'string';
            });
            picked = 0;
            draw();
        } else if (message.type === 'result' && typeof message.text === 'string') {
            showResult(message.kind, message.text);
        } else if (message.type === 'focus') {
            line.focus();
        }
    }

    if (window.external && typeof window.external.receiveMessage === 'function') {
        window.external.receiveMessage(receive);
    }

    line.addEventListener('input', function () {
        if (result.className === 'error') {
            showResult('', '');
        }

        send({ type: 'complete', line: line.value });
    });

    line.addEventListener('keydown', function (event) {
        if (event.key === 'Escape') {
            event.preventDefault();
            close();
        } else if (event.key === 'Enter') {
            event.preventDefault();
            send({ type: 'run', line: line.value });
        } else if (event.key === 'Tab') {
            event.preventDefault();
            if (items.length > 0) {
                line.value = items[picked].line;
                send({ type: 'complete', line: line.value });
            }
        } else if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
            event.preventDefault();
            if (items.length > 0) {
                picked = (picked + (event.key === 'ArrowDown' ? 1 : items.length - 1)) % items.length;
                draw();
            }
        } else if (result.className === 'error') {
            showResult('', '');
        }
    });

    window.addEventListener('blur', close);
    document.addEventListener('contextmenu', function (event) {
        if (event.target !== line) {
            event.preventDefault();
        }
    });

    line.focus();
}());
