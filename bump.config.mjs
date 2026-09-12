import { spawnSync } from 'node:child_process';
import { readVersion, updateVersions } from './scripts/release-version.mjs';

export default {
  currentVersion: readVersion(),
  // The execute hook handles C# attributes and Thunderstore's version_number.
  files: [],
  commit: 'Release v{version}',
  tag: 'v{version}',
  // Push the release explicitly after reviewing the version commit.
  push: false,
  noGitCheck: false,
  printCommits: false,
  execute(operation) {
    const tag = `v${operation.state.newVersion}`;
    const result = spawnSync('git', ['show-ref', '--verify', '--quiet', `refs/tags/${tag}`], { cwd: operation.options.cwd });
    if (result.status === 0) {
      throw new Error(`Tag ${tag} already exists. Use an unused release version. Inherited upstream tags must not be overwritten. No version files were changed.`);
    }
    if (result.status !== 1) {
      throw new Error(`Could not check whether ${tag} exists. Check Git access before releasing. No version files were changed.`);
    }
    const updatedFiles = updateVersions(operation.state.newVersion, operation.options.cwd);
    operation.update({ updatedFiles });
  },
};
