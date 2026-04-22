#[cfg(test)]
mod tests {
    use lsw_lib::config::LswConfig;

    #[test]
    fn defaults_are_sane() {
        let cfg = LswConfig::default();
        assert!(cfg.defaults.memory_mb >= 1024);
        assert!(cfg.defaults.cpus >= 1);
    }
}
