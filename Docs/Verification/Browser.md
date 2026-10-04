# Driving a web front end from the browser

With the built-in browser tools (or Chrome): navigate to the dev server, then read the page as text or as an accessibility tree rather than screenshots. Snippets for the page's console (javascript tool):

```js
// the visible rows of the first table, as text
[...document.querySelectorAll('table tbody tr')].map(r => r.innerText.replace(/\s+/g, ' ').trim())

// every input of an open dialog with its limits: what the schema says a field accepts
[...document.querySelectorAll('dialog input, [role=dialog] input, mat-dialog-container input')]
  .map(i => ({ name: i.name || i.id || i.placeholder, type: i.type, min: i.min, max: i.max, maxLength: i.maxLength, required: i.required }))

// set a value the way a framework notices it (React ignores a plain `.value =`)
function setValue(el, text) {
  const setter = Object.getOwnPropertyDescriptor(Object.getPrototypeOf(el), 'value').set;
  setter.call(el, text);
  el.dispatchEvent(new Event('input', { bubbles: true }));
  el.dispatchEvent(new Event('change', { bubbles: true }));
}

// the API calls the page made (a failing one shows its status)
performance.getEntriesByType('resource').filter(r => r.name.includes('/api/')).map(r => `${r.responseStatus ?? ''} ${r.name}`)
```
