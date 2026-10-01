import { Button } from "../../components/Button";
import type { ArchiveInfo } from "../../services/backupApi";
import { formatBytes } from "./backupMessages";

/**
 * Disaster-recovery entry point: the retained backup files found on disk, listed WITHOUT needing any backup
 * history (a replacement server has none). Copy the retained .abk file into the server's import folder and it
 * appears here; choosing it opens the same restore wizard, which restores into an isolated copy.
 */
export function ArchivePanel({ archives, onRestore }: { archives: ArchiveInfo[]; onRestore: (a: ArchiveInfo) => void }) {
  return (
    <section className="alv-config-panel" aria-label="Recover from a retained backup file">
      <h2 className="alv-config-panel__form-title">Recover from a retained backup file</h2>
      <p>
        Use this on a replacement server, or when the backup history is unavailable. Copy the retained <code>.abk</code> file into this server&apos;s import folder; it is listed below with no
        history needed. You will need the recovery key file and passphrase you stored offline.
      </p>
      {archives.length === 0 ? (
        <p>No backup files were found in the backup or import folders.</p>
      ) : (
        <table className="alv-config-panel__table">
          <thead>
            <tr>
              <th>File</th>
              <th>Folder</th>
              <th>Modified</th>
              <th>Size</th>
              <th>Status</th>
              <th>Actions</th>
            </tr>
          </thead>
          <tbody>
            {archives.map((a) => (
              <tr key={a.ref}>
                <td>{a.fileName}</td>
                <td>{a.location === "import" ? "Import folder" : "Backup folder"}</td>
                <td>{new Date(a.modifiedUtc).toLocaleString()}</td>
                <td>{formatBytes(a.sizeBytes)}</td>
                <td>{!a.readable ? "Not a readable backup" : a.inHistory ? "In history" : "Not in history"}</td>
                <td>
                  <Button disabled={!a.readable} onClick={() => onRestore(a)} aria-label={`Restore from ${a.fileName}`}>
                    Restore from this file
                  </Button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  );
}
