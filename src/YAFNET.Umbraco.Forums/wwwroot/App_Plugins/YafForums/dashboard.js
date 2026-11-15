// Backoffice dashboard of YAF.NET Forums: links to the forum and its administration.
// The forum is administered in YAF itself, by a member that is a YAF administrator.
// Plain JavaScript (no build step), using the modules of the Umbraco backoffice import map.
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { css, html } from '@umbraco-cms/backoffice/external/lit';

const links = [
  { href: '/Forums', title: 'Open the forum', text: 'The forum as your site members see it.' },
  {
    href: '/Forums/Admin/Admin',
    title: 'Forum administration',
    text: 'Boards, forums, users and settings. Requires a member that is a YAF administrator.'
  },
  { href: '/Forums/Admin/EventLog', title: 'Forum event log', text: 'Errors and events logged by YAF.NET.' }
];

export class YafForumsDashboardElement extends UmbLitElement {
  render() {
    return html`
      <uui-box headline="YAF.NET Forums" class="intro">
        <p>
          The forum runs at <a href="/Forums" target="_blank" rel="noopener">/Forums</a>.
          Site members log in to it with their member account (single sign-on).
        </p>
      </uui-box>
      <div class="grid">
        ${links.map(
          (link) => html`
            <uui-box headline=${link.title}>
              <p>${link.text}</p>
              <a href=${link.href} target="_blank" rel="noopener">Open ↗</a>
            </uui-box>`
        )}
      </div>
    `;
  }

  static styles = css`
    :host { display: block; padding: var(--uui-size-layout-1); }
    .intro { margin-bottom: var(--uui-size-space-5); }
    .grid { display: grid; gap: var(--uui-size-space-5); grid-template-columns: repeat(auto-fill, minmax(260px, 1fr)); }
    p { margin: 0 0 var(--uui-size-space-4); }
    a { font-weight: 600; }
  `;
}

customElements.define('yaf-forums-dashboard', YafForumsDashboardElement);

export const element = YafForumsDashboardElement;
export default YafForumsDashboardElement;
