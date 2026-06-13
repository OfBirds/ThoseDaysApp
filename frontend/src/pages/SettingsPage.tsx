import { useEffect, useState } from 'react';
import { useAuth } from '../context/AuthContext';
import DataSection from '../components/DataSection';
import '../styles/settings.css';

interface Prefs {
  notifyReleases: boolean;
  notifyPeriodReminder: boolean;
  reminderLeadDays: number;
}

const LEAD_MIN = 1;
const LEAD_MAX = 7;

function SettingsPage() {
  const { user } = useAuth();
  const [prefs, setPrefs] = useState<Prefs | null>(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!user) return;
    let alive = true;
    (async () => {
      try {
        const res = await fetch(`/api/user/${user.id}/prefs`);
        if (!res.ok) throw new Error('load failed');
        const data = (await res.json()) as Prefs;
        if (alive) setPrefs(data);
      } catch {
        if (alive) setError("Couldn't load your settings. Please try again.");
      } finally {
        if (alive) setLoading(false);
      }
    })();
    return () => {
      alive = false;
    };
  }, [user]);

  const update = async (next: Prefs) => {
    if (!user) return;
    const previous = prefs;
    setPrefs(next); // optimistic
    setSaving(true);
    setError(null);
    try {
      const res = await fetch(`/api/user/${user.id}/prefs`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(next),
      });
      if (!res.ok) throw new Error('save failed');
      setPrefs((await res.json()) as Prefs);
    } catch {
      setPrefs(previous); // roll back on failure — never silently lose the real state
      setError("Couldn't save that change. Please try again.");
    } finally {
      setSaving(false);
    }
  };

  if (!user) return null;

  return (
    <div className="settings-page">
      <h1 className="settings-title">Settings</h1>

      {loading ? (
        <p className="settings-muted">Loading your settings…</p>
      ) : (
        <section className="settings-section" aria-labelledby="settings-notifications-heading">
          <h2 id="settings-notifications-heading" className="settings-section-title">
            Notifications
          </h2>

          <label
            className="settings-row"
            title="We'll send a reminder a few days before your next expected period. You can turn this off any time, including from a link in the email."
          >
            <input
              type="checkbox"
              checked={prefs?.notifyPeriodReminder ?? false}
              disabled={saving || !prefs}
              onChange={(e) => prefs && update({ ...prefs, notifyPeriodReminder: e.target.checked })}
            />
            <span className="settings-row-label">
              Email me before my period
              <span className="settings-row-help">
                A gentle reminder a few days ahead, based on your recent cycles. Email only — no
                data leaves the app. Needs an email on file and email set up by your host.
              </span>
            </span>
          </label>

          {prefs?.notifyPeriodReminder && (
            <label className="settings-row settings-subrow" title="How early the reminder arrives before your expected period.">
              <span className="settings-row-label">How many days before?</span>
              <input
                type="number"
                aria-label="Days before my period"
                min={LEAD_MIN}
                max={LEAD_MAX}
                value={prefs.reminderLeadDays}
                disabled={saving}
                onChange={(e) => {
                  const n = Math.min(LEAD_MAX, Math.max(LEAD_MIN, Number(e.target.value) || LEAD_MIN));
                  update({ ...prefs, reminderLeadDays: n });
                }}
              />
            </label>
          )}

          <label className="settings-row" title="Get an email when a new version of ThoseDays is released. You can turn this off any time, including from a link in the email.">
            <input
              type="checkbox"
              checked={prefs?.notifyReleases ?? false}
              disabled={saving || !prefs}
              onChange={(e) => prefs && update({ ...prefs, notifyReleases: e.target.checked })}
            />
            <span className="settings-row-label">
              Email me about new versions
              <span className="settings-row-help">
                A short note when ThoseDays is updated. Nothing else — and no data leaves the app.
              </span>
            </span>
          </label>

          {error && (
            <p className="settings-error" role="alert">
              {error}
            </p>
          )}
        </section>
      )}

      <DataSection userId={user.id} />

      <section className="settings-section settings-support" aria-labelledby="settings-support-heading">
        <h2 id="settings-support-heading" className="settings-section-title">
          Support this project
        </h2>
        <p className="settings-row-help">
          ThoseDays is free and open source, built for self-hosting. If it's useful to you and
          you'd like to chip in, it's appreciated — never required.
        </p>
        <div className="settings-support-links">
          <a
            className="settings-support-link paypal"
            href="https://paypal.me/fun3fun"
            target="_blank"
            rel="noopener noreferrer"
          >
            Donate via PayPal
          </a>
          <a
            className="settings-support-link patreon"
            href="https://patreon.com/3fun"
            target="_blank"
            rel="noopener noreferrer"
          >
            Support on Patreon
          </a>
        </div>
      </section>
    </div>
  );
}

export default SettingsPage;
