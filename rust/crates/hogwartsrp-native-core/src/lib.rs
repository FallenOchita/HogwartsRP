//! Deterministic primitives for performance-sensitive HogwartsRP components.
//! This crate does not access the game process or mutate game state.

pub const MAX_RESOURCE_NAME_BYTES: usize = 64;
pub const MAX_EVENT_NAME_BYTES: usize = 128;
pub const MAX_PROTOCOL_FRAME_BYTES: usize = 32 * 1024;
pub const MAX_EVENT_ARGUMENTS: usize = 32;

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum ValidationError { Empty, TooLong, InvalidCharacters, TooManyArguments, FrameTooLarge }

pub fn validate_resource_name(value: &str) -> Result<(), ValidationError> {
    if value.is_empty() { return Err(ValidationError::Empty); }
    if value.len() > MAX_RESOURCE_NAME_BYTES { return Err(ValidationError::TooLong); }
    if !value.bytes().all(|c| c.is_ascii_alphanumeric() || c == b'_' || c == b'-') {
        return Err(ValidationError::InvalidCharacters);
    }
    Ok(())
}

pub fn validate_event_name(value: &str) -> Result<(), ValidationError> {
    if value.is_empty() { return Err(ValidationError::Empty); }
    if value.len() > MAX_EVENT_NAME_BYTES { return Err(ValidationError::TooLong); }
    if !value.bytes().all(|c| c.is_ascii_alphanumeric() || matches!(c, b'_' | b'-' | b':' | b'.')) {
        return Err(ValidationError::InvalidCharacters);
    }
    Ok(())
}

pub fn validate_event_argument_count(count: usize) -> Result<(), ValidationError> {
    if count > MAX_EVENT_ARGUMENTS { return Err(ValidationError::TooManyArguments); }
    Ok(())
}

pub fn validate_protocol_frame(bytes: &[u8]) -> Result<(), ValidationError> {
    if bytes.len() > MAX_PROTOCOL_FRAME_BYTES { return Err(ValidationError::FrameTooLarge); }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn validates_resource_names() {
        assert_eq!(validate_resource_name("common_utils"), Ok(()));
        assert_eq!(validate_resource_name("../outside"), Err(ValidationError::InvalidCharacters));
        assert_eq!(validate_resource_name(""), Err(ValidationError::Empty));
    }
    #[test]
    fn validates_event_names_and_limits() {
        assert_eq!(validate_event_name("player:loaded.v1"), Ok(()));
        assert_eq!(validate_event_name("bad event"), Err(ValidationError::InvalidCharacters));
        assert_eq!(validate_event_name(&"x".repeat(MAX_EVENT_NAME_BYTES + 1)), Err(ValidationError::TooLong));
    }
    #[test]
    fn enforces_argument_and_frame_bounds() {
        assert_eq!(validate_event_argument_count(MAX_EVENT_ARGUMENTS), Ok(()));
        assert_eq!(validate_event_argument_count(MAX_EVENT_ARGUMENTS + 1), Err(ValidationError::TooManyArguments));
        assert_eq!(validate_protocol_frame(&vec![0; MAX_PROTOCOL_FRAME_BYTES]), Ok(()));
        assert_eq!(validate_protocol_frame(&vec![0; MAX_PROTOCOL_FRAME_BYTES + 1]), Err(ValidationError::FrameTooLarge));
    }
}
